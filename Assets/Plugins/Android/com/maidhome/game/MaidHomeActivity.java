package com.maidhome.game;

import android.app.Activity;
import android.content.Intent;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.net.Uri;
import android.os.Bundle;

import com.unity3d.player.UnityPlayer;
import com.unity3d.player.UnityPlayerActivity;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;

/**
 * 主 Activity：只是 UnityPlayerActivity 加了一个「起系统照片选择器」。
 *
 * 为什么非得自己建 Activity：startActivityForResult 的结果只会回到 activity 的 onActivityResult，
 * Unity 自带的 UnityPlayerActivity 不转发给插件，所以插件类接不到选中的图。
 * 清单里 Assets/Plugins/Android/AndroidManifest.xml 的主 Activity 已经换成这个类。
 *
 * 选择器用 ACTION_GET_CONTENT（Android 4.4 起都有，不需要任何权限）；拿到的 content:// 没法直接读，
 * 先整份拷到应用 cache 目录，再把绝对路径 UnitySendMessage 回 C#（PhotoPicker.OnPhotoPicked）。
 */
public class MaidHomeActivity extends UnityPlayerActivity {

    /** 请求码，随便取的 */
    private static final int PICK_REQUEST = 0x4D49;

    /** C# 那边的接收者物体名，选完把结果发给它 */
    private static String pendingReceiver;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
    }

    /** C# 侧（PhotoPicker）调它弹系统照片选择器；返回有没有真的起来 */
    public static boolean openPhotoPicker(String receiverName) {
        Activity activity = UnityPlayer.currentActivity;
        if (!(activity instanceof MaidHomeActivity)) {
            return false;
        }

        pendingReceiver = receiverName;

        Intent intent = new Intent(Intent.ACTION_GET_CONTENT);
        intent.setType("image/*");
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        try {
            activity.startActivityForResult(Intent.createChooser(intent, "选一张照片"), PICK_REQUEST);
            return true;
        } catch (Exception noPicker) {
            pendingReceiver = null;
            return false;
        }
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != PICK_REQUEST) {
            return;
        }

        String path = "";
        if (resultCode == RESULT_OK && data != null && data.getData() != null) {
            path = copyToCache(data.getData());
        }

        String receiver = pendingReceiver;
        pendingReceiver = null;
        if (receiver != null && receiver.length() > 0) {
            // 空串 = 取消或失败，C# 那边按"没选"处理
            UnityPlayer.UnitySendMessage(receiver, "OnPhotoPicked", path);
        }
    }

    /**
     * content:// 没法当文件读，整份拷到 cache 里再给路径。
     * 相册里常见 HEIC / WebP，Unity 2020 的 Texture2D.LoadImage 只稳吃 PNG/JPG，
     * 所以这里先让 Android 自己解码，再统一压成 PNG 回传给 C#。
     */
    private String copyToCache(Uri uri) {
        InputStream input = null;
        OutputStream output = null;
        File raw = null;
        try {
            input = getContentResolver().openInputStream(uri);
            if (input == null) {
                return "";
            }

            raw = new File(getCacheDir(), "maid_photo_pick_src");
            output = new FileOutputStream(raw);
            byte[] buffer = new byte[16384];
            int read;
            while ((read = input.read(buffer)) > 0) {
                output.write(buffer, 0, read);
            }
            output.flush();
            close(output);
            output = null;

            Bitmap bitmap = BitmapFactory.decodeFile(raw.getAbsolutePath());
            if (bitmap == null) {
                // 系统解不了就保留原始文件，让 C# 再试一次并给出具体错误
                return raw.getAbsolutePath();
            }

            File png = new File(getCacheDir(), "maid_photo_pick.png");
            OutputStream pngOutput = null;
            try {
                pngOutput = new FileOutputStream(png);
                if (bitmap.compress(Bitmap.CompressFormat.PNG, 100, pngOutput)) {
                    pngOutput.flush();
                    return png.getAbsolutePath();
                }
            } catch (Exception failed) {
                // 转 PNG 失败就退回原始文件
            } finally {
                close(pngOutput);
                bitmap.recycle();
            }

            return raw.getAbsolutePath();
        } catch (Exception failed) {
            return "";
        } finally {
            close(input);
            close(output);
        }
    }

    private static void close(java.io.Closeable stream) {
        if (stream == null) {
            return;
        }

        try {
            stream.close();
        } catch (Exception ignored) {
            // 关不上也就算了
        }
    }
}
