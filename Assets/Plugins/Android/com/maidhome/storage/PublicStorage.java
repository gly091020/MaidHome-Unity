package com.maidhome.storage;

import android.Manifest;
import android.app.Activity;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Build;
import android.os.Environment;
import android.provider.Settings;

import com.unity3d.player.UnityPlayer;

import java.io.File;
import java.io.IOException;

/**
 * 公共 Documents 目录的 Java 桥（C# 侧 PublicStorage.cs 调它）。
 *
 * Android 11+ 用 MANAGE_EXTERNAL_STORAGE（"所有文件访问"），10 及以下用 WRITE_EXTERNAL_STORAGE。
 * 拿不到权限、或者探针写不进去（比如 Android 10 上被 Scoped Storage 挡住）时，
 * getDocumentsPath() 返回 null，C# 那边会退回应用私有目录。
 */
public final class PublicStorage {

    /** 申请 WRITE_EXTERNAL_STORAGE 用的请求码，随便取的 */
    private static final int WRITE_REQUEST_CODE = 0x4D48;

    private PublicStorage() {
    }

    /** 外部存储挂着才算能用 */
    public static boolean isSupported() {
        return Environment.MEDIA_MOUNTED.equals(Environment.getExternalStorageState());
    }

    public static boolean hasAccess() {
        Activity activity = UnityPlayer.currentActivity;
        if (activity == null) {
            return false;
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            return Environment.isExternalStorageManager();
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            return activity.checkSelfPermission(Manifest.permission.WRITE_EXTERNAL_STORAGE)
                    == PackageManager.PERMISSION_GRANTED;
        }

        return true;
    }

    /**
     * 申请权限：Android 11+ 打开"所有文件访问"的系统页（用户自己开开关），
     * 10 及以下弹运行时权限对话框。返回有没有真的发起。
     */
    public static boolean requestAccess() {
        final Activity activity = UnityPlayer.currentActivity;
        if (activity == null) {
            return false;
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            try {
                Intent intent = new Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION,
                        Uri.parse("package:" + activity.getPackageName()));
                activity.startActivity(intent);
                return true;
            } catch (Exception notFound) {
                try {
                    activity.startActivity(new Intent(Settings.ACTION_MANAGE_ALL_FILES_ACCESS_PERMISSION));
                    return true;
                } catch (Exception alsoFailed) {
                    return false;
                }
            }
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            activity.requestPermissions(new String[] { Manifest.permission.WRITE_EXTERNAL_STORAGE },
                    WRITE_REQUEST_CODE);
            return true;
        }

        return false;
    }

    /**
     * 公共 Documents 的绝对路径。真能写才返回，写不了返回 null —— C# 侧就是靠 null 决定退回私有目录。
     */
    public static String getDocumentsPath() {
        if (!isSupported() || !hasAccess()) {
            return null;
        }

        File documents = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOCUMENTS);
        if (documents == null) {
            return null;
        }

        File root = new File(documents, "MaidHome");
        if (!root.exists() && !root.mkdirs()) {
            return null;
        }

        File probe = new File(root, ".write_probe");
        try {
            if (probe.exists() && !probe.delete()) {
                return null;
            }
            if (!probe.createNewFile()) {
                return null;
            }
            if (!probe.delete()) {
                return null;
            }
        } catch (IOException blocked) {
            return null;
        }

        return documents.getAbsolutePath();
    }
}
