using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace MaidHome.Core.Storage
{
    /// <summary>
    /// 「选一张照片」的平台层，照片相关的 #if 全关在这一个文件里：
    /// - 编辑器 / Windows：调 comdlg32 的「打开文件」对话框。同步弹（选完立刻回调，期间画面被对话框挡住是正常的），
    ///   不依赖 System.Windows.Forms —— 打包出来的播放器里没有那个程序集，P/Invoke 才稳
    /// - Android 真机：由自建的 MaidHomeActivity 起系统照片选择器（ACTION_GET_CONTENT），选完把图拷进应用 cache，
    ///   再 UnitySendMessage 回来。**回调是异步的**，可能晚几帧、也可能已经切到别的界面了，调用方要自己认状态
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PhotoPicker : MonoBehaviour
    {
        /// <summary>回调参数：选中的文件绝对路径；空串 = 取消 / 失败</summary>
        public delegate void ResultHandler(string path);

        const string ReceiverName = "MaidPhotoPicker";

        static PhotoPicker _instance;
        static ResultHandler _pending;

        public static bool IsSupported
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return true;
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>弹选择器。同一次只认一个回调，再点一次会把上一次的顶掉</summary>
        public static void Pick(ResultHandler handler)
        {
            if (handler == null)
            {
                return;
            }

            _pending = handler;

#if UNITY_ANDROID && !UNITY_EDITOR
            EnsureInstance();
            bool started = false;
            try
            {
                using (AndroidJavaClass activity = new AndroidJavaClass("com.maidhome.game.MaidHomeActivity"))
                {
                    started = activity.CallStatic<bool>("openPhotoPicker", ReceiverName);
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning("[照片] 起不了系统选择器：" + error.Message
                    + "（检查 Assets/Plugins/Android 下的 MaidHomeActivity.java 有没有编进包里、"
                    + "清单里的主 Activity 是不是换成了它）");
            }

            if (!started)
            {
                Finish("");
            }
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            Finish(PickOnWindows());
#else
            Debug.LogWarning("[照片] 这个平台还没有系统选择器");
            Finish("");
#endif
        }

        /// <summary>Android 的 Java 侧 UnitySendMessage 调进来（方法名要和 java 里写的一致）</summary>
        public void OnPhotoPicked(string path)
        {
            Finish(path);
        }

        static void Finish(string path)
        {
            ResultHandler handler = _pending;
            _pending = null;
            if (handler != null)
            {
                handler(path ?? "");
            }
        }

        static void EnsureInstance()
        {
            if (_instance != null)
            {
                return;
            }

            _instance = FindObjectOfType<PhotoPicker>();
            if (_instance != null)
            {
                return;
            }

            GameObject host = new GameObject(ReceiverName);
            _instance = host.AddComponent<PhotoPicker>();
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        const int OFN_FILEMUSTEXIST = 0x00001000;
        const int OFN_PATHMUSTEXIST = 0x00000800;
        const int OFN_NOCHANGEDIR = 0x00000008;
        const int OFN_EXPLORER = 0x00080000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct OpenFileName
        {
            public int structSize;
            public IntPtr owner;
            public IntPtr instance;
            public string filter;
            public string customFilter;
            public int maxCustomFilter;
            public int filterIndex;
            public string file;
            public int maxFile;
            public string fileTitle;
            public int maxFileTitle;
            public string initialDir;
            public string title;
            public int flags;
            public short fileOffset;
            public short fileExtension;
            public string defExt;
            public IntPtr customData;
            public IntPtr hook;
            public string templateName;
            public IntPtr reserved;
            public int reservedInt;
            public int flagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetOpenFileNameW(ref OpenFileName ofn);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        /// <summary>同步弹系统「打开文件」对话框；取消返回空串</summary>
        static string PickOnWindows()
        {
            const int bufferChars = 4096;
            OpenFileName ofn = new OpenFileName();
            ofn.structSize = Marshal.SizeOf(typeof(OpenFileName));
            ofn.owner = GetForegroundWindow();
            ofn.filter = "图片 (*.png;*.jpg;*.jpeg)\0*.png;*.jpg;*.jpeg\0所有文件 (*.*)\0*.*\0\0";
            ofn.filterIndex = 1;
            ofn.file = new string('\0', bufferChars);
            ofn.maxFile = ofn.file.Length;
            ofn.fileTitle = new string('\0', 256);
            ofn.maxFileTitle = ofn.fileTitle.Length;
            ofn.title = "选一张照片";
            ofn.flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_EXPLORER;

            try
            {
                if (!GetOpenFileNameW(ref ofn))
                {
                    return "";
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning("[照片] 打开文件对话框失败：" + error.Message);
                return "";
            }

            return ofn.file == null ? "" : ofn.file.TrimEnd('\0');
        }
#endif
    }
}
