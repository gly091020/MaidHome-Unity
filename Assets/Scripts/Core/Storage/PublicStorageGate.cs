using UnityEngine;

namespace MaidHome.Core.Storage
{
    /// <summary>
    /// Android 真机专用的授权小助手：启动时自动问一次「所有文件访问」权限，
    /// 回到前台时重算存档目录（玩家可能刚在系统设置里给了权限，或者把权限撤了）。
    /// 编辑器和其他平台不会创建这个对象。
    ///
    /// 权限没给也不影响玩：存档先落在应用私有目录，给了以后旧的会自动补拷到公共目录。
    /// 想让玩家手动再问一次（比如设置界面加个按钮）就调 PublicStorage.RequestPermission()。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PublicStorageGate : MonoBehaviour
    {
        const string AskedKey = "maidhome.public_storage_asked";

        static PublicStorageGate _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Spawn()
        {
            if (!PublicStorage.NeedsPermission || _instance != null)
            {
                return;
            }

            GameObject holder = new GameObject("PublicStorageGate");
            DontDestroyOnLoad(holder);
            holder.AddComponent<PublicStorageGate>();
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
        }

        void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        void Start()
        {
            if (PublicStorage.HasPermission)
            {
                AppPaths.Refresh();
                return;
            }

            // 一次安装只自动问一次：问多了烦，玩家不想给就用私有目录
            if (PlayerPrefs.GetInt(AskedKey, 0) != 0)
            {
                return;
            }

            PlayerPrefs.SetInt(AskedKey, 1);
            PlayerPrefs.Save();
            RequestPermission();
        }

        /// <summary>再问一次权限（设置界面的按钮接这个）</summary>
        public void RequestPermission()
        {
            if (PublicStorage.RequestPermission())
            {
                return;
            }

            Debug.LogWarning("[存档] 没能打开系统授权页，继续用当前目录：" + AppPaths.SavesRoot);
        }

        void OnApplicationFocus(bool focused)
        {
            if (focused)
            {
                // 从系统设置页回来
                AppPaths.Refresh();
            }
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused)
            {
                AppPaths.Refresh();
            }
        }
    }
}
