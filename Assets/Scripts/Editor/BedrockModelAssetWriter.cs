using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MaidHome.EditorTools
{
    /// <summary>把生成出来的模型落成工程里的资源：贴图导入设置、网格、材质、Prefab。</summary>
    public static class BedrockModelAssetWriter
    {
        public static Texture2D ImportTexture(string sourcePath, string targetFolder)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                return null;
            }

            EnsureFolder(targetFolder);
            string fileName = Path.GetFileName(sourcePath);
            string targetPath = targetFolder.TrimEnd('/') + "/" + fileName;
            if (!File.Exists(targetPath))
            {
                File.Copy(sourcePath, targetPath, false);
                AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceUpdate);
            }

            TextureImporter importer = AssetImporter.GetAtPath(targetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.filterMode = FilterMode.Point;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(targetPath);
        }

        public static GameObject SaveAsPrefab(GameObject root, string targetFolder, string assetName)
        {
            if (root == null)
            {
                return null;
            }

            EnsureFolder(targetFolder);
            string folder = targetFolder.TrimEnd('/');
            string meshFolder = folder + "/" + assetName + "_Meshes";
            EnsureFolder(meshFolder);

            List<MeshFilter> filters = new List<MeshFilter>();
            root.GetComponentsInChildren(true, filters);
            HashSet<Object> saved = new HashSet<Object>();
            for (int i = 0; i < filters.Count; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh == null || saved.Contains(mesh))
                {
                    continue;
                }

                if (AssetDatabase.Contains(mesh))
                {
                    saved.Add(mesh);
                    continue;
                }

                string path = AssetDatabase.GenerateUniqueAssetPath(meshFolder + "/" + Sanitize(mesh.name) + ".asset");
                AssetDatabase.CreateAsset(mesh, path);
                saved.Add(mesh);
            }

            List<MeshRenderer> renderers = new List<MeshRenderer>();
            root.GetComponentsInChildren(true, renderers);
            for (int i = 0; i < renderers.Count; i++)
            {
                Material material = renderers[i].sharedMaterial;
                if (material == null || saved.Contains(material))
                {
                    continue;
                }

                if (!AssetDatabase.Contains(material))
                {
                    string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Sanitize(material.name) + ".mat");
                    AssetDatabase.CreateAsset(material, path);
                }

                saved.Add(material);
            }

            AssetDatabase.SaveAssets();
            string prefabPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Sanitize(assetName) + ".prefab");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            AssetDatabase.Refresh();
            return prefab;
        }

        public static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        public static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "unnamed";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            char[] buffer = name.ToCharArray();
            for (int i = 0; i < buffer.Length; i++)
            {
                for (int j = 0; j < invalid.Length; j++)
                {
                    if (buffer[i] == invalid[j])
                    {
                        buffer[i] = '_';
                        break;
                    }
                }
            }

            return new string(buffer);
        }
    }
}
