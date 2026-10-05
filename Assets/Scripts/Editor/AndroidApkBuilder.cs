using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MaidHome.EditorTools
{
    /// <summary>
    /// 一键出 Android 包：临时把脚本后端切成 IL2CPP、架构只勾 ARM64，编译完把 apk 拷到桌面
    /// （文件名 = PlayerSettings.productName + bundleVersion + .apk）。
    /// 不管成功、失败还是中途报错，都会在 finally 里把脚本后端 / 架构 / AAB 开关还原成原来的样子。
    /// 只动这三项，包名、签名、版本号、场景列表都不碰。
    /// </summary>
    public static class AndroidApkBuilder
    {
        const string MenuPath = "Tools/MaidHome/打包 Android APK（IL2CPP + ARM64）";

        // 旧设置存在 SessionState 里：万一改设置/构建时 Unity 重新加载了脚本域（那 finally 就跑不到），
        // 下面那个 InitializeOnLoadMethod 还能补一次还原，别把工程留成 IL2CPP + ARM64
        const string DirtyKey = "MaidHome.ApkBuilder.Dirty";
        const string BackendKey = "MaidHome.ApkBuilder.Backend";
        const string ArchKey = "MaidHome.ApkBuilder.Architectures";
        const string AppBundleKey = "MaidHome.ApkBuilder.AppBundle";

        [InitializeOnLoadMethod]
        static void RestoreInterrupted()
        {
            if (!SessionState.GetBool(DirtyKey, false))
            {
                return;
            }

            RestoreOldSettings();
            Debug.LogWarning("[打包] 上一次打包中途被打断（脚本域重新加载），已经把临时切成的 IL2CPP / ARM64 还原回去");
        }

        [MenuItem(MenuPath, false, 40)]
        public static void BuildApk()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            {
                EditorUtility.DisplayDialog("打包失败",
                    "没装 Android Build Support（还得和你这个 c1 版编辑器同源同版本）。", "知道了");
                return;
            }

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                bool switchTarget = EditorUtility.DisplayDialog("先切到 Android 平台",
                    "当前平台是 " + EditorUserBuildSettings.activeBuildTarget
                    + "，要先切到 Android（会重新导入一遍资源，可能要几分钟）。继续？",
                    "切过去并打包", "取消");
                if (!switchTarget)
                {
                    return;
                }

                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                {
                    Debug.LogError("[打包] 切换到 Android 平台失败");
                    return;
                }
            }

            string[] scenes = EnabledScenes();
            if (scenes.Length == 0)
            {
                Debug.LogError("[打包] Build Settings 里一个启用的场景都没有");
                return;
            }

            // 记下旧设置，finally 里还原
            ScriptingImplementation oldBackend = PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android);
            AndroidArchitecture oldArchitectures = PlayerSettings.Android.targetArchitectures;
            bool oldAppBundle = EditorUserBuildSettings.buildAppBundle;
            SaveOldSettings(oldBackend, oldArchitectures, oldAppBundle);

            string tempDir = Path.Combine(Path.GetTempPath(), "MaidHomeApk");
            string tempApk = Path.Combine(tempDir, "build.apk");
            string apkName = SafeFileName(PlayerSettings.productName + PlayerSettings.bundleVersion) + ".apk";
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string target = Path.Combine(desktop, apkName);

            try
            {
                PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                EditorUserBuildSettings.buildAppBundle = false;
                AssetDatabase.SaveAssets();

                Debug.Log("[打包] 脚本后端 = " + PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android)
                    + "，架构 = " + PlayerSettings.Android.targetArchitectures
                    + "，场景 " + scenes.Length + " 个，输出 " + apkName);

                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }

                Directory.CreateDirectory(tempDir);

                BuildPlayerOptions options = new BuildPlayerOptions();
                options.scenes = scenes;
                options.locationPathName = tempApk;
                options.target = BuildTarget.Android;
                options.targetGroup = BuildTargetGroup.Android;
                options.options = BuildOptions.None;

                BuildReport report = BuildPipeline.BuildPlayer(options);
                if (report == null || report.summary.result != BuildResult.Succeeded)
                {
                    string reason = report != null
                        ? report.summary.result + "（错误 " + report.summary.totalErrors + " 条）"
                        : "没有拿到构建报告";
                    Debug.LogError("[打包] 失败：" + reason);
                    EditorUtility.DisplayDialog("打包失败", reason + "\n细节看 Console。设置已经还原。", "知道了");
                    return;
                }

                File.Copy(tempApk, target, true);
                double mb = report.summary.totalSize / 1024.0 / 1024.0;
                Debug.Log("[打包] 成功：" + target + "（" + mb.ToString("0.0") + " MB）");
                EditorUtility.DisplayDialog("打包完成",
                    apkName + "\n已经放到桌面\n" + mb.ToString("0.0") + " MB", "好");
                EditorUtility.RevealInFinder(target);
            }
            catch (Exception e)
            {
                Debug.LogError("[打包] 出错：" + e);
                EditorUtility.DisplayDialog("打包出错", e.Message + "\n\n设置已经还原，细节看 Console。", "知道了");
            }
            finally
            {
                RestoreOldSettings();
            }
        }

        static void SaveOldSettings(ScriptingImplementation backend, AndroidArchitecture architectures, bool appBundle)
        {
            SessionState.SetBool(DirtyKey, true);
            SessionState.SetInt(BackendKey, (int)backend);
            SessionState.SetInt(ArchKey, (int)architectures);
            SessionState.SetBool(AppBundleKey, appBundle);
        }

        static void RestoreOldSettings()
        {
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android,
                (ScriptingImplementation)SessionState.GetInt(BackendKey, (int)ScriptingImplementation.Mono2x));
            PlayerSettings.Android.targetArchitectures =
                (AndroidArchitecture)SessionState.GetInt(ArchKey, (int)AndroidArchitecture.ARMv7);
            EditorUserBuildSettings.buildAppBundle = SessionState.GetBool(AppBundleKey, false);
            AssetDatabase.SaveAssets();
            SessionState.SetBool(DirtyKey, false);
            Debug.Log("[打包] 设置已还原：后端 = " + PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android)
                + "，架构 = " + PlayerSettings.Android.targetArchitectures
                + "，AAB = " + EditorUserBuildSettings.buildAppBundle);
        }

        static string[] EnabledScenes()
        {
            List<string> scenes = new List<string>();
            EditorBuildSettingsScene[] all = EditorBuildSettings.scenes;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].enabled)
                {
                    scenes.Add(all[i].path);
                }
            }

            return scenes.ToArray();
        }

        /// <summary>软件名里可能有 Windows 不允许的字符（/ : 之类），换成下划线</summary>
        static string SafeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++)
            {
                name = name.Replace(invalid[i], '_');
            }

            return string.IsNullOrEmpty(name) ? "MaidHome" : name;
        }
    }
}
