using MaidHome.Core.Audio;
using MaidHome.Core.Settings;
using MaidHome.Gameplay.Audio;
using MaidHome.Gameplay.Portal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MaidHome.Gameplay.Settings
{
    /// <summary>把全局设置应用到实际系统：背景音乐、女仆音效、传送门端口。</summary>
    [DisallowMultipleComponent]
    public sealed class GameSettingsApplier : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindObjectOfType<GameSettingsApplier>() != null)
            {
                return;
            }

            GameObject root = new GameObject("GameSettingsApplier");
            root.AddComponent<GameSettingsApplier>();
        }

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            GameSettingsStore.Changed += ApplyAll;
            SceneManager.sceneLoaded += OnSceneLoaded;
            ApplyAll();
        }

        void OnDestroy()
        {
            GameSettingsStore.Changed -= ApplyAll;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode)
        {
            ApplyAll();
        }

        static void ApplyAll()
        {
            GameSettings settings = GameSettingsStore.Current;
            if (BackgroundMusic.Instance != null)
            {
                BackgroundMusic.Instance.Volume = Mathf.Clamp01(settings.BackgroundVolume / 100f);
            }

            MaidAudioService.MasterVolume = Mathf.Clamp01(settings.MaidVolume / 100f);

            PortalController[] portals = FindObjectsOfType<PortalController>(true);
            for (int i = 0; i < portals.Length; i++)
            {
                portals[i].SetPort(settings.PortalPort);
            }
        }
    }
}
