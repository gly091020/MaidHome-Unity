using System;
using UnityEngine;

namespace MaidHome.Core.Platform
{
    /// <summary>
    /// Android 震动强度：Java 桥用 VibrationEffect 的 amplitude；其他平台返回 false。
    /// 平台差异只留在这个文件里，玩法层只调 Vibrate。
    /// </summary>
    public static class MaidVibration
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        const string JavaClass = "com.maidhome.haptics.VibrationBridge";

        static AndroidJavaClass _bridge;
        static bool _failed;

        public static bool Vibrate(int strengthPercent, int milliseconds)
        {
            if (strengthPercent <= 0)
            {
                return false;
            }

            AndroidJavaClass bridge = Bridge();
            if (bridge == null)
            {
                return false;
            }

            try
            {
                return bridge.CallStatic<bool>("vibrate", strengthPercent, milliseconds);
            }
            catch (Exception error)
            {
                Debug.LogWarning("[震动] 调 Android 桥失败：" + error.Message);
                return false;
            }
        }

        public static bool HasAmplitudeControl()
        {
            return CallBool("hasAmplitudeControl");
        }

        public static bool HasVibrator()
        {
            return CallBool("hasVibrator");
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
                Debug.LogWarning("[震动] 调 Android 桥 " + method + " 失败：" + error.Message);
                return false;
            }
        }

        static AndroidJavaClass Bridge()
        {
            if (_bridge == null && !_failed)
            {
                try
                {
                    _bridge = new AndroidJavaClass(JavaClass);
                }
                catch (Exception error)
                {
                    _failed = true;
                    Debug.LogWarning("[震动] 没找到 Android 桥 " + JavaClass + "：" + error.Message
                        + "（检查 VibrationBridge.java 有没有编进包里）");
                }
            }

            return _bridge;
        }
#else
        public static bool Vibrate(int strengthPercent, int milliseconds)
        {
            return false;
        }

        public static bool HasAmplitudeControl()
        {
            return false;
        }

        public static bool HasVibrator()
        {
            return false;
        }
#endif
    }
}
