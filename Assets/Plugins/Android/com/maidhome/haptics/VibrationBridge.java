package com.maidhome.haptics;

import android.app.Activity;
import android.content.Context;
import android.os.Build;
import android.os.VibrationEffect;
import android.os.Vibrator;

import com.unity3d.player.UnityPlayer;

/**
 * Android 震动桥：API 26 起用 VibrationEffect 的 amplitude 控制强度。
 * 不支持振幅控制的设备只能使用系统默认强度。
 */
public final class VibrationBridge {
    private VibrationBridge() {
    }

    public static boolean hasVibrator() {
        Vibrator vibrator = vibrator();
        return vibrator != null && vibrator.hasVibrator();
    }

    public static boolean hasAmplitudeControl() {
        Vibrator vibrator = vibrator();
        return Build.VERSION.SDK_INT >= Build.VERSION_CODES.O
                && vibrator != null && vibrator.hasVibrator() && vibrator.hasAmplitudeControl();
    }

    /**
     * strengthPercent: 0~100；0 不震。
     * milliseconds: 持续时间，最短 1ms。
     */
    public static boolean vibrate(int strengthPercent, int milliseconds) {
        if (strengthPercent <= 0) {
            return false;
        }

        Vibrator vibrator = vibrator();
        if (vibrator == null || !vibrator.hasVibrator()) {
            return false;
        }

        int duration = Math.max(1, milliseconds);
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                int amplitude = VibrationEffect.DEFAULT_AMPLITUDE;
                if (vibrator.hasAmplitudeControl()) {
                    int percent = Math.max(1, Math.min(100, strengthPercent));
                    amplitude = Math.max(1, Math.round(255f * percent / 100f));
                }

                vibrator.vibrate(VibrationEffect.createOneShot(duration, amplitude));
            } else {
                // Android 7 及以下没有 amplitude 接口
                vibrator.vibrate(duration);
            }

            return true;
        } catch (Exception ignored) {
            return false;
        }
    }

    private static Vibrator vibrator() {
        Activity activity = UnityPlayer.currentActivity;
        if (activity == null) {
            return null;
        }

        return (Vibrator) activity.getSystemService(Context.VIBRATOR_SERVICE);
    }
}
