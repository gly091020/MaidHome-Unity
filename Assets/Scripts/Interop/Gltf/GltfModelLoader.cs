using System;
using System.IO;
using System.Threading.Tasks;
using GLTFast;
using GLTFast.Logging;
using UnityEngine;

namespace MaidHome.Interop.Gltf
{
    /// <summary>
    /// 从外部文件加载静态 glTF/glb，材质走 McGltfMaterialGenerator 直接生成 MC 风格。
    /// 只在运行时用：失败时靠 Object.Destroy 清理，编辑器里别直接调。
    /// .glb 单文件最省事；.gltf 配外部 .bin/.png 时按文件所在目录当 baseUri 找。
    /// </summary>
    public static class GltfModelLoader
    {
        public static async Task<GameObject> LoadAsync(string path, Transform parent = null, float scale = 1f)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.LogError("glTF 文件不存在: " + path);
                return null;
            }

            GameObject root = new GameObject(Path.GetFileNameWithoutExtension(path));
            root.transform.SetParent(parent, false);
            root.transform.localScale = Vector3.one * scale;
            GltfImportHolder holder = root.AddComponent<GltfImportHolder>();

            ImportSettings settings = new ImportSettings();
            settings.GenerateMipMaps = false;

            GltfImport import = new GltfImport(null, null, new McGltfMaterialGenerator(), new ConsoleLogger());
            try
            {
                if (!await import.Load(File.ReadAllBytes(path), BaseUri(path), settings))
                {
                    Debug.LogError("glTF 解析失败: " + path);
                    import.Dispose();
                    UnityEngine.Object.Destroy(root);
                    return null;
                }

                if (!await import.InstantiateMainSceneAsync(root.transform))
                {
                    Debug.LogError("glTF 场景实例化失败: " + path);
                    import.Dispose();
                    UnityEngine.Object.Destroy(root);
                    return null;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("glTF 加载异常 " + path + ": " + e);
                import.Dispose();
                UnityEngine.Object.Destroy(root);
                return null;
            }

            holder.Hold(import);
            return root;
        }

        static Uri BaseUri(string path)
        {
            try
            {
                string directory = Path.GetDirectoryName(Path.GetFullPath(path));
                if (string.IsNullOrEmpty(directory))
                {
                    return null;
                }

                return new Uri(directory + Path.DirectorySeparatorChar);
            }
            catch
            {
                // 拼不出 baseUri 就只当单文件处理，.glb 不受影响
                return null;
            }
        }
    }
}
