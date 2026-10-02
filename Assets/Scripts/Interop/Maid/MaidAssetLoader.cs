using System.Collections.Generic;
using System.IO;
using MaidHome.Core.Storage;
using MaidHome.Interop.Bedrock;
using UnityEngine;

namespace MaidHome.Interop.Maid
{
    /// <summary>
    /// 女仆存档 -&gt; Unity 资源：读 maid.json 指向的基岩模型/贴图/动画，转成网格 + AnimationClip。
    /// 有缓存就先读缓存（MaidAssetCache），没有就转一遍再写进缓存。
    /// 动画会先铺一层共用的 maid.animation.json，再用模型自带的那份按名字覆盖（TLM 的行为）。
    /// </summary>
    public static class MaidAssetLoader
    {
        public static float PixelsPerUnit = 16f;
        public static float SampleRate = 30f;
        public static bool MergeSharedAnimations = true;

        /// <summary>模型里约定要藏起来的节点：FOX 是单独的狐狸实体占位，其余是 TLM 的忽略部位。</summary>
        static readonly string[] HiddenNodes =
        {
            "FOX",
            "ahoge",
            "begShow",
            "blink",
            "blink2",
            "hurtBlink",
            "danmakuAttackShow",
        };

        /// <summary>共用的那套基础动画（TLM 里随模组走），目前放在存档根目录。</summary>
        public static string SharedAnimationPath()
        {
            string beside = Path.Combine(AppPaths.SavesRoot, "maid.animation.json");
            if (File.Exists(beside))
            {
                return beside;
            }

            string inside = Path.Combine(AppPaths.MaidSaveRoot, "maid.animation.json");
            return File.Exists(inside) ? inside : beside;
        }

        public static MaidAssets Load(MaidSaveData maid)
        {
            MaidAssets cached;
            if (MaidAssetCache.TryLoad(maid, out cached))
            {
                return cached;
            }

            MaidAssets assets = Build(maid);
            if (assets.Root == null || assets.ClipData.Count == 0)
            {
                return assets;
            }

            try
            {
                MaidAssetCache.Write(maid, assets);
            }
            catch (System.Exception error)
            {
                assets.Warnings.Add("写缓存失败: " + error.Message);
            }

            return assets;
        }

        /// <summary>不管缓存，重新转一遍。</summary>
        public static MaidAssets Build(MaidSaveData maid)
        {
            MaidAssets assets = new MaidAssets();
            assets.Maid = maid;
            float start = Time.realtimeSinceStartup;

            if (maid == null)
            {
                assets.Warnings.Add("女仆数据为空");
                return assets;
            }

            assets.Warnings.AddRange(maid.Warnings);

            if (string.IsNullOrEmpty(maid.ModelPath) || !File.Exists(maid.ModelPath))
            {
                assets.Warnings.Add("没有可用的模型文件");
                return assets;
            }

            BedrockGeometry geometry;
            try
            {
                geometry = BedrockGeometry.ParseFile(maid.ModelPath);
            }
            catch (System.Exception error)
            {
                assets.Warnings.Add("模型解析失败: " + error.Message);
                return assets;
            }

            Texture2D texture = MaidAssetCache.LoadTexture(maid.TexturePath);
            BedrockModelBuilder modelBuilder = new BedrockModelBuilder();
            modelBuilder.PixelsPerUnit = PixelsPerUnit;
            modelBuilder.RotationSigns = Vector3.one;
            modelBuilder.RotationOrder = BedrockRotationOrder.ZYX;
            modelBuilder.RootName = string.IsNullOrEmpty(maid.Name) ? maid.Id : maid.Name;

            BedrockModelBuildResult model = modelBuilder.Build(geometry, texture);
            assets.Root = model.Root;
            assets.Texture = texture;
            assets.Warnings.AddRange(model.Warnings);
            ApplyModelRules(assets);

            BedrockAnimationSet animations = LoadAnimations(maid, assets.Warnings);
            BedrockAnimationClipBuilder clipBuilder = new BedrockAnimationClipBuilder();
            clipBuilder.PixelsPerUnit = PixelsPerUnit;
            clipBuilder.SampleRate = SampleRate;

            // pre_parallel 是常驻并行层（尾巴摆动、长发飘），它动的骨骼别的动画一条曲线都不写，
            // 否则那些骨骼会被主动画的常量曲线钉死，并行层就白播了
            HashSet<string> parallelBones = CollectParallelBones(animations.Animations);

            int emptyClips = 0;
            for (int i = 0; i < animations.Animations.Count; i++)
            {
                BedrockAnimation animation = animations.Animations[i];
                if (!MatchesModel(animation, geometry))
                {
                    emptyClips++;
                    continue;
                }

                clipBuilder.SkipBones = BedrockAnimation.IsParallelName(animation.Name)
                    ? NonAuthoredBones(geometry, animation)
                    : ParallelBonesExcluding(parallelBones, animation);
                BedrockClipData data = clipBuilder.BuildData(animation, geometry);
                assets.ClipData.Add(data);
                assets.Clips.Add(clipBuilder.BuildClip(data));
            }

            if (emptyClips > 0)
            {
                assets.Warnings.Add("有 " + emptyClips + " 条动画一根骨骼都对不上这个模型，已跳过");
            }

            assets.BuildSeconds = Time.realtimeSinceStartup - start;
            return assets;
        }

        /// <summary>所有 pre_parallel 动画动过的骨骼，去重后的并集。</summary>
        static HashSet<string> CollectParallelBones(List<BedrockAnimation> animations)
        {
            HashSet<string> bones = new HashSet<string>();
            for (int i = 0; i < animations.Count; i++)
            {
                BedrockAnimation animation = animations[i];
                if (!BedrockAnimation.IsParallelName(animation.Name))
                {
                    continue;
                }

                for (int b = 0; b < animation.Bones.Count; b++)
                {
                    bones.Add(animation.Bones[b].Name);
                }
            }

            return bones;
        }

        /// <summary>
        /// 常驻动画只烘它自己动过的骨骼。并行层是运行时按轨道直接写骨骼的，
        /// 铺上没动过的骨骼反而会把别的并行层（比如 pre_parallel0 摆的尾巴）盖成静止姿势。
        /// </summary>
        static HashSet<string> NonAuthoredBones(BedrockGeometry geometry, BedrockAnimation animation)
        {
            HashSet<string> skip = new HashSet<string>();
            for (int i = 0; i < geometry.Bones.Count; i++)
            {
                string name = geometry.Bones[i].Name;
                if (animation.FindBone(name) == null)
                {
                    skip.Add(name);
                }
            }

            return skip;
        }

        /// <summary>
        /// 并行层只补主动画没动的骨骼：主动画自己动到的（sleep 会收尾巴、run 会甩尾巴）让它说了算，
        /// 次序和 TLM 一致。idle / walk 本来就不碰尾巴，所以常驻摆动只在它们身上生效。
        /// </summary>
        static HashSet<string> ParallelBonesExcluding(HashSet<string> parallelBones, BedrockAnimation animation)
        {
            HashSet<string> skip = new HashSet<string>(parallelBones);
            for (int i = 0; i < animation.Bones.Count; i++)
            {
                skip.Remove(animation.Bones[i].Name);
            }

            return skip;
        }

        /// <summary>
        /// 套用硬编码的模型规则：按 maid.json 的 scale 缩放根节点、关掉要藏的节点。
        /// 这两件事都不进缓存（scale 改了不该重转一次），所以**建完模型要调、读缓存回来也要再调**。
        /// </summary>
        public static void ApplyModelRules(MaidAssets assets)
        {
            if (assets == null || assets.Root == null)
            {
                return;
            }

            float scale = assets.Maid == null ? 1f : assets.Maid.Scale;
            assets.Root.transform.localScale = new Vector3(scale, scale, scale);

            Transform[] transforms = assets.Root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                string name = transforms[i].name;
                for (int j = 0; j < HiddenNodes.Length; j++)
                {
                    if (string.Equals(name, HiddenNodes[j], System.StringComparison.OrdinalIgnoreCase))
                    {
                        transforms[i].gameObject.SetActive(false);
                        if (!assets.HiddenNodes.Contains(name))
                        {
                            assets.HiddenNodes.Add(name);
                        }
                    }
                }
            }
        }

        static BedrockAnimationSet LoadAnimations(MaidSaveData maid, List<string> warnings)
        {
            BedrockAnimationSet merged = new BedrockAnimationSet();

            if (MergeSharedAnimations)
            {
                string shared = SharedAnimationPath();
                if (File.Exists(shared))
                {
                    Merge(merged, BedrockAnimationSet.ParseFile(shared), warnings);
                }
                else
                {
                    warnings.Add("没找到共用的动画文件: " + shared);
                }
            }

            if (!string.IsNullOrEmpty(maid.AnimationPath) && File.Exists(maid.AnimationPath))
            {
                Merge(merged, BedrockAnimationSet.ParseFile(maid.AnimationPath), warnings);
            }
            else
            {
                warnings.Add("模型自带的动画文件不存在: " + maid.AnimationFile);
            }

            merged.FormatVersion = maid.AnimationFile + " + shared";
            return merged;
        }

        /// <summary>同名动画按后加载的覆盖（模型自带的盖掉共用的）。</summary>
        static void Merge(BedrockAnimationSet target, BedrockAnimationSet source, List<string> warnings)
        {
            for (int i = 0; i < source.Warnings.Count; i++)
            {
                warnings.Add(source.Warnings[i]);
            }

            for (int i = 0; i < source.Animations.Count; i++)
            {
                BedrockAnimation animation = source.Animations[i];
                bool replaced = false;
                for (int j = 0; j < target.Animations.Count; j++)
                {
                    if (target.Animations[j].Name == animation.Name)
                    {
                        target.Animations[j] = animation;
                        replaced = true;
                        break;
                    }
                }

                if (!replaced)
                {
                    target.Animations.Add(animation);
                }
            }
        }

        static bool MatchesModel(BedrockAnimation animation, BedrockGeometry geometry)
        {
            for (int i = 0; i < animation.Bones.Count; i++)
            {
                string name = animation.Bones[i].Name;
                for (int j = 0; j < geometry.Bones.Count; j++)
                {
                    if (geometry.Bones[j].Name == name)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
