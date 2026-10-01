using System.IO;
using UnityEditor;
using UnityEngine;

namespace MaidHome.EditorTools
{
    /// <summary>把烘出来的 AnimationClip 落成工程里的 .anim 资源，方便直接看曲线。</summary>
    public static class BedrockAnimationAssetWriter
    {
        public static AnimationClip SaveClip(AnimationClip clip, string targetFolder, string assetName)
        {
            if (clip == null)
            {
                return null;
            }

            BedrockModelAssetWriter.EnsureFolder(targetFolder);
            string path = targetFolder.TrimEnd('/') + "/" + BedrockModelAssetWriter.Sanitize(assetName) + ".anim";
            if (File.Exists(path))
            {
                AssetDatabase.DeleteAsset(path);
            }

            AssetDatabase.CreateAsset(clip, path);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        }
    }
}
