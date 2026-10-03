using System;
using UnityEngine;

namespace MaidHome.Core.Storage
{
    /// <summary>
    /// 「公共文档目录」的访问入口，平台差异都关在这一个文件里，业务代码只问它、不写 #if。
    ///
    /// - 编辑器 / Windows：就是我的文档，随时可写，不需要授权
    /// - Android 真机：/storage/emulated/0/Documents/MaidHome，需要「所有文件访问」权限
    ///   （Android 11+ 是 MANAGE_EXTERNAL_STORAGE，10 及以下是 WRITE_EXTERNAL_STORAGE）
    ///
    /// 拿不到权限时 DocumentsRoot 返回 null，调用方（AppPaths）就退回应用私有目录。
    /// 注意：DocumentsRoot / HasPermission / RequestPermission 都要在主线程调（会走 JNI）。
    /// </summary>
    public static class PublicStorage
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        const string JavaClass = "com.maidhome.storage.PublicStorage";

        static AndroidJavaClass _bridge;
        static bool _bridgeFailed;

        /// <summary>这个平台需要运行时授权（Android 真机才有这回事）</summary>
        public static bool NeedsPermission
        {
            get { return true; }
        }

        public static bool HasPermission
        {
            get { return CallBool("hasAccess"); }
        }

        /// <summary>能写才给路径，不能写返回 null</summary>
        public static string DocumentsRoot
        {
            get { return CallString("getDocumentsPath"); }
        }

        public static bool RequestPermission()
        {
            return CallBool("requestAccess");
        }

        public static string Describe()
        {
            return "公共 Documents（Android，需要「所有文件访问」权限）";
        }

        static AndroidJavaClass Bridge()
        {
            if (_bridge == null && !_bridgeFailed)
            {
                try
                {
                    _bridge = new AndroidJavaClass(JavaClass);
                }
                catch (Exception error)
                {
                    _bridgeFailed = true;
                    Debug.LogWarning("[存储] 没找到 Android 桥 " + JavaClass + "：" + error.Message
                        + "（检查 Assets/Plugins/Android 下的 PublicStorage.java 有没有编进包里）");
                }
            }

            return _bridge;
        }

        static bool CallBool(string method)
        {
            AndroidJavaClass bridge = Bridge();
            if (bridge == null)
            {
                return false;
            }

            try
            {
                return bridge.CallStatic<bool>(method);
            }
            catch (Exception error)
            {
                Debug.LogWarning("[存储] 调 " + method + " 失败：" + error.Message);
                return false;
            }
        }

        static string CallString(string method)
        {
            AndroidJavaClass bridge = Bridge();
            if (bridge == null)
            {
                return null;
            }

            try
            {
                return bridge.CallStatic<string>(method);
            }
            catch (Exception error)
            {
                Debug.LogWarning("[存储] 调 " + method + " 失败：" + error.Message);
                return null;
            }
        }
#else
        /// <summary>编辑器 / Windows 不需要授权</summary>
        public static bool NeedsPermission
        {
            get { return false; }
        }

        public static bool HasPermission
        {
            get { return true; }
        }

        public static string DocumentsRoot
        {
            get
            {
                string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                // 解析不到就返回 null，让 AppPaths 退回应用私有目录（别退回相对路径）
                return string.IsNullOrEmpty(documents) ? null : documents;
            }
        }

        public static bool RequestPermission()
        {
            return false;
        }

        public static string Describe()
        {
            return "我的文档（Windows / 编辑器）";
        }
#endif
    }
}
