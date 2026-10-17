using System.IO;
using UnityEngine;

namespace MaidHome.Interop.Maid
{
    /// <summary>
    /// 面具上贴的那张照片：把玩家选的图片裁成正方形、缩到 512、写成女仆文件夹里的 mask.png。
    /// 本工程没有相册 API（Android 那边靠 MANAGE_EXTERNAL_STORAGE 直接读目录），所以这里只管两件事：
    /// 从任意图片文件产出一张贴图，以及按女仆落盘 / 读回来。
    /// </summary>
    public static class MaidMaskPhoto
    {
        /// <summary>照片存成正方形多大：面具正面那块板本来就是方的，512 足够、再大只是白占内存</summary>
        public const int Side = 512;
        public const string FileName = "mask.png";

        public static string PathOf(MaidSaveData maid)
        {
            if (maid == null || string.IsNullOrEmpty(maid.Folder))
            {
                return "";
            }

            return Path.Combine(maid.Folder, FileName);
        }

        /// <summary>读回来（给面具当贴图）。文件不在、读坏了都返回 null</summary>
        public static Texture2D Load(MaidSaveData maid)
        {
            string path = PathOf(maid);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(File.ReadAllBytes(path)))
                {
                    Object.Destroy(texture);
                    return null;
                }

                // 照片不做像素风：平滑采样看着才像张照片
                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Clamp;
                return texture;
            }
            catch (System.Exception error)
            {
                Debug.LogWarning("[面具] 照片读不出来：" + error.Message + "（" + path + "）");
                return null;
            }
        }

        /// <summary>
        /// 把 sourcePath 这张图裁成正方形缩到 Side，落到女仆文件夹的 mask.png。
        /// 成功返回文件名，失败返回空串并给出 error。
        /// </summary>
        public static string Import(MaidSaveData maid, string sourcePath, out string error)
        {
            error = "";
            string target = PathOf(maid);
            if (string.IsNullOrEmpty(target))
            {
                error = "这只女仆没有存档目录";
                return "";
            }

            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                error = "这张照片找不到了";
                return "";
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(sourcePath);
            }
            catch (System.Exception exception)
            {
                error = "读不了这张照片：" + exception.Message;
                return "";
            }

            Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!source.LoadImage(bytes))
            {
                Object.Destroy(source);
                Debug.LogWarning("[面具] 图片解码失败，文件大小 " + bytes.Length + " 字节：" + sourcePath);
                error = "这不是一张能认出来的图片（只认 png / jpg）";
                return "";
            }

            Texture2D cropped = CropToSquare(source);
            Object.Destroy(source);
            if (cropped == null)
            {
                error = "图片处理失败（尺寸不合法？）";
                return "";
            }

            byte[] png = cropped.EncodeToPNG();
            Object.Destroy(cropped);

            string temp = target + ".tmp";
            try
            {
                File.WriteAllBytes(temp, png);
                if (File.Exists(target))
                {
                    File.Delete(target);
                }

                File.Move(temp, target);
            }
            catch (System.Exception exception)
            {
                error = "照片写不进她的文件夹：" + exception.Message;
                return "";
            }

            return FileName;
        }

        /// <summary>居中裁成正方形再缩到 Side：横图切两边、竖图切上下，不拉伸</summary>
        static Texture2D CropToSquare(Texture2D source)
        {
            if (source.width < 2 || source.height < 2)
            {
                return null;
            }

            float aspect = (float)source.width / source.height;
            Vector2 scale = aspect >= 1f ? new Vector2(1f / aspect, 1f) : new Vector2(1f, aspect);
            Vector2 offset = (Vector2.one - scale) * 0.5f;

            RenderTexture buffer = RenderTexture.GetTemporary(Side, Side, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, buffer, scale, offset);
                RenderTexture.active = buffer;
                Texture2D result = new Texture2D(Side, Side, TextureFormat.RGBA32, false);
                result.ReadPixels(new Rect(0f, 0f, Side, Side), 0, 0);
                result.Apply();
                result.filterMode = FilterMode.Bilinear;
                result.wrapMode = TextureWrapMode.Clamp;
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(buffer);
            }
        }
    }
}
