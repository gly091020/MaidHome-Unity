using System.IO;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Networking;
#endif

namespace MaidHome.Core.Storage
{
    /// <summary>
    /// 读随包资源 Assets/StreamingAssets 的平台层（平台差异都收在这一个文件里，和 PublicStorage 一个路子）。
    /// Windows / 编辑器：就是硬盘上的普通文件，直接 File 读；
    /// Android：StreamingAssets 在 apk 里，硬盘上没有这个路径，只能走 UnityWebRequest——
    /// 所以这里返回的是 Task，调用方在 async 流程里 await（房子加载本来就是 async）。
    /// </summary>
    public static class StreamingAssetsStorage
    {
        /// <summary>读一个随包文件，读不到返回 null（路径用 / 分隔，相对 StreamingAssets）</summary>
        public static async Task<byte[]> ReadAllBytesAsync(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                return null;
            }

            relativePath = relativePath.Replace('\\', '/');
#if UNITY_ANDROID && !UNITY_EDITOR
            string url = Application.streamingAssetsPath + "/" + relativePath;
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                TaskCompletionSource<bool> done = new TaskCompletionSource<bool>();
                request.SendWebRequest().completed += _ => done.TrySetResult(true);
                await done.Task;

                if (request.result != UnityWebRequest.Result.Success || request.downloadHandler == null)
                {
                    Debug.LogWarning("读随包资源失败: " + relativePath + "（" + request.error + "）");
                    return null;
                }

                return request.downloadHandler.data;
            }
#else
            string path = Path.Combine(Application.streamingAssetsPath,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
#endif
        }
    }
}
