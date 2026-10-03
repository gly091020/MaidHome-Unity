using System.Collections.Generic;
using UnityEngine;

namespace MaidHome.Interop.Bedrock
{
    /// <summary>
    /// 把基岩动画烘焙成 Unity 的 AnimationClip（legacy 曲线，绑在骨骼 Transform 上）。
    ///
    /// 不直接把关键帧塞进欧拉角曲线的原因：基岩的旋转是「静止欧拉角 + 动画欧拉角逐轴相加，
    /// 再按 ZYX 合成」，和 Unity 的 localEulerAngles 不是一套；直接写欧拉角必然错位。
    /// 所以按采样率算出四元数，写成 localRotation.x/y/z/w 四条曲线。
    ///
    /// 采样只铺到「最后一个关键帧」为止：那之后的值是常量，再按 30fps 铺点纯属浪费
    /// （maid.animation.json 里有 animation_length = 1000 的动画）。被 anim_time 表达式驱动的
    /// 动画除外，那种整条都要铺。
    ///
    /// 每条 track 默认覆盖模型里的**所有**骨骼：动画没动的骨骼写一条常量曲线钉在静止姿势上，
    /// 否则上一条动画留下的姿势会残留在没被覆盖的骨骼上。例外见 SkipBones。
    /// </summary>
    public sealed class BedrockAnimationClipBuilder
    {
        /// <summary>只影响 clip.frameRate；运行时按需建 clip 时拿这个当采样率</summary>
        public static float DefaultSampleRate = 30f;

        public float PixelsPerUnit = 16f;
        public Vector3 RotationSigns = Vector3.one;
        public BedrockRotationOrder RotationOrder = BedrockRotationOrder.ZYX;
        public float SampleRate = 30f;

        /// <summary>
        /// 这些骨骼不写曲线。常驻的 pre_parallel 动画独占它们（尾巴摆动、头发飘），
        /// 别的动画要是也钉一条常量曲线，并行层的值就被压在下面看不出来了。
        /// </summary>
        public HashSet<string> SkipBones;

        enum Part
        {
            Position,
            Rotation,
            Scale,
        }

        struct BoneSample
        {
            public float Time;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
        }

        public AnimationClip Build(BedrockAnimation animation, BedrockGeometry geometry)
        {
            return BuildClip(BuildData(animation, geometry));
        }

        public BedrockClipData BuildData(BedrockAnimation animation, BedrockGeometry geometry)
        {
            BedrockClipData data = new BedrockClipData();
            if (animation == null || geometry == null)
            {
                return data;
            }

            float rate = Mathf.Max(1f, SampleRate);
            float length = EffectiveLength(animation, rate);
            data.Name = animation.Name;
            data.Length = length;
            data.WrapMode = animation.Loop == BedrockAnimationLoop.Loop ? WrapMode.Loop : WrapMode.ClampForever;

            Dictionary<string, BedrockBone> geometryBones = new Dictionary<string, BedrockBone>();
            for (int i = 0; i < geometry.Bones.Count; i++)
            {
                if (!geometryBones.ContainsKey(geometry.Bones[i].Name))
                {
                    geometryBones.Add(geometry.Bones[i].Name, geometry.Bones[i]);
                }
            }

            Dictionary<string, BedrockAnimationBone> animatedBones = new Dictionary<string, BedrockAnimationBone>();
            for (int i = 0; i < animation.Bones.Count; i++)
            {
                if (!animatedBones.ContainsKey(animation.Bones[i].Name))
                {
                    animatedBones.Add(animation.Bones[i].Name, animation.Bones[i]);
                }
            }

            List<float> times = CollectSampleTimes(animation, length, rate);
            MolangContext context = new MolangContext();
            List<BoneSample> samples = new List<BoneSample>();

            for (int i = 0; i < geometry.Bones.Count; i++)
            {
                BedrockBone bone = geometry.Bones[i];
                if (SkipBones != null && SkipBones.Contains(bone.Name))
                {
                    continue;
                }

                BedrockAnimationBone animated;
                if (!animatedBones.TryGetValue(bone.Name, out animated))
                {
                    animated = null;
                }

                bool moves = animated != null && (animated.Rotation.Present || animated.Position.Present || animated.Scale.Present);
                int count = moves ? times.Count : 1;

                Vector3 parentPivot = Vector3.zero;
                BedrockBone parent;
                if (!string.IsNullOrEmpty(bone.Parent) && geometryBones.TryGetValue(bone.Parent, out parent))
                {
                    parentPivot = parent.Pivot;
                }

                Vector3 restPosition = BedrockModelBuilder.ConvertPosition(bone.Pivot - parentPivot, PixelsPerUnit);
                Vector3 restEuler = bone.Rotation;
                Quaternion previous = Quaternion.identity;

                samples.Clear();
                for (int s = 0; s < count; s++)
                {
                    float time = moves ? times[s] : 0f;
                    context.AnimTime = time;
                    Vector3 euler = restEuler;
                    Vector3 offset = Vector3.zero;
                    Vector3 scale = Vector3.one;
                    if (animated != null)
                    {
                        if (animated.Rotation.Present)
                        {
                            euler = restEuler + animated.Rotation.Sample(time, context);
                        }

                        if (animated.Position.Present)
                        {
                            offset = animated.Position.Sample(time, context);
                        }

                        if (animated.Scale.Present)
                        {
                            scale = animated.Scale.Sample(time, context);
                        }
                    }

                    Quaternion rotation = BedrockModelBuilder.ComposeRotation(euler, RotationSigns, RotationOrder);
                    // 相邻采样点的四元数必须落在同一个半球，否则线性插值会绕一大圈
                    if (s > 0 && Quaternion.Dot(previous, rotation) < 0f)
                    {
                        rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
                    }

                    previous = rotation;
                    BoneSample sample = new BoneSample();
                    sample.Time = time;
                    sample.Rotation = rotation;
                    sample.Position = restPosition + BedrockModelBuilder.ConvertPosition(offset, PixelsPerUnit);
                    sample.Scale = scale;
                    samples.Add(sample);
                }

                data.Tracks.Add(MakeTrack(BuildPath(bone, geometryBones), samples));
            }

            return data;
        }

        public AnimationClip BuildClip(BedrockClipData data)
        {
            AnimationClip clip = new AnimationClip();
            if (data == null)
            {
                return clip;
            }

            clip.name = data.Name;
            clip.frameRate = Mathf.Max(1f, SampleRate);
            clip.legacy = true;
            clip.wrapMode = data.WrapMode;

            for (int i = 0; i < data.Tracks.Count; i++)
            {
                BedrockBoneTrack track = data.Tracks[i];
                clip.SetCurve(track.Path, typeof(Transform), "localPosition.x", BuildCurve(track, Part.Position, 0));
                clip.SetCurve(track.Path, typeof(Transform), "localPosition.y", BuildCurve(track, Part.Position, 1));
                clip.SetCurve(track.Path, typeof(Transform), "localPosition.z", BuildCurve(track, Part.Position, 2));
                clip.SetCurve(track.Path, typeof(Transform), "localRotation.x", BuildCurve(track, Part.Rotation, 0));
                clip.SetCurve(track.Path, typeof(Transform), "localRotation.y", BuildCurve(track, Part.Rotation, 1));
                clip.SetCurve(track.Path, typeof(Transform), "localRotation.z", BuildCurve(track, Part.Rotation, 2));
                clip.SetCurve(track.Path, typeof(Transform), "localRotation.w", BuildCurve(track, Part.Rotation, 3));
                clip.SetCurve(track.Path, typeof(Transform), "localScale.x", BuildCurve(track, Part.Scale, 0));
                clip.SetCurve(track.Path, typeof(Transform), "localScale.y", BuildCurve(track, Part.Scale, 1));
                clip.SetCurve(track.Path, typeof(Transform), "localScale.z", BuildCurve(track, Part.Scale, 2));
            }

            clip.EnsureQuaternionContinuity();
            return clip;
        }

        /// <summary>没有长度也没有关键帧的动画在 TLM 里长度是「无限」：被 anim_time 驱动的按估出来的周期烘一个循环，其余当静止姿势。</summary>
        static float EffectiveLength(BedrockAnimation animation, float rate)
        {
            float length = animation.Length;
            if (length <= 1e-6f)
            {
                length = animation.ProceduralPeriod > 0f ? animation.ProceduralPeriod : 1f / rate;
            }

            return Mathf.Max(length, 1f / rate);
        }

        static BedrockBoneTrack MakeTrack(string path, List<BoneSample> samples)
        {
            BedrockBoneTrack track = new BedrockBoneTrack();
            track.Path = path;
            track.Times = new float[samples.Count];
            track.Positions = new Vector3[samples.Count];
            track.Rotations = new Quaternion[samples.Count];
            track.Scales = new Vector3[samples.Count];
            for (int i = 0; i < samples.Count; i++)
            {
                track.Times[i] = samples[i].Time;
                track.Positions[i] = samples[i].Position;
                track.Rotations[i] = samples[i].Rotation;
                track.Scales[i] = samples[i].Scale;
            }

            return track;
        }

        static AnimationCurve BuildCurve(BedrockBoneTrack track, Part part, int axis)
        {
            UnityEngine.Keyframe[] keys = new UnityEngine.Keyframe[track.Count];
            for (int i = 0; i < track.Count; i++)
            {
                keys[i] = new UnityEngine.Keyframe(track.Times[i], Value(track, part, axis, i));
            }

            AnimationCurve curve = new AnimationCurve(keys);
            curve.preWrapMode = WrapMode.Clamp;
            curve.postWrapMode = WrapMode.Clamp;
            return curve;
        }

        static float Value(BedrockBoneTrack track, Part part, int axis, int index)
        {
            if (part == Part.Rotation)
            {
                Quaternion value = track.Rotations[index];
                return axis == 0 ? value.x : axis == 1 ? value.y : axis == 2 ? value.z : value.w;
            }

            if (part == Part.Scale)
            {
                Vector3 scale = track.Scales[index];
                return axis == 0 ? scale.x : axis == 1 ? scale.y : scale.z;
            }

            Vector3 position = track.Positions[index];
            return axis == 0 ? position.x : axis == 1 ? position.y : position.z;
        }

        static List<float> CollectSampleTimes(BedrockAnimation animation, float length, float rate)
        {
            float denseEnd = DenseEnd(animation, length);
            List<float> times = new List<float>();
            int steps = Mathf.CeilToInt(denseEnd * rate);
            for (int i = 0; i <= steps; i++)
            {
                times.Add(Mathf.Clamp(i / rate, 0f, denseEnd));
            }

            for (int i = 0; i < animation.Bones.Count; i++)
            {
                BedrockAnimationBone bone = animation.Bones[i];
                AddKeyTimes(times, bone.Rotation, length);
                AddKeyTimes(times, bone.Position, length);
                AddKeyTimes(times, bone.Scale, length);
            }

            times.Add(0f);
            times.Add(denseEnd);
            times.Add(length);
            times.Sort();

            List<float> unique = new List<float>(times.Count);
            for (int i = 0; i < times.Count; i++)
            {
                if (unique.Count > 0 && times[i] - unique[unique.Count - 1] <= 1e-5f)
                {
                    continue;
                }

                unique.Add(times[i]);
            }

            return unique;
        }

        /// <summary>需要按采样率铺点的区间：有 anim_time 表达式就铺满全片，否则只铺到最后一个关键帧。</summary>
        static float DenseEnd(BedrockAnimation animation, float length)
        {
            float denseEnd = 0f;
            for (int i = 0; i < animation.Bones.Count; i++)
            {
                BedrockAnimationBone bone = animation.Bones[i];
                if (HasExpression(bone.Rotation) || HasExpression(bone.Position) || HasExpression(bone.Scale))
                {
                    return length;
                }

                denseEnd = Mathf.Max(denseEnd, LastKeyTime(bone.Rotation));
                denseEnd = Mathf.Max(denseEnd, LastKeyTime(bone.Position));
                denseEnd = Mathf.Max(denseEnd, LastKeyTime(bone.Scale));
            }

            return Mathf.Min(denseEnd, length);
        }

        static bool HasExpression(BedrockChannel channel)
        {
            if (!channel.Present || channel.Keys.Count > 0)
            {
                return false;
            }

            return channel.Constant.XExpression != null || channel.Constant.YExpression != null || channel.Constant.ZExpression != null;
        }

        static float LastKeyTime(BedrockChannel channel)
        {
            return channel.Keys.Count == 0 ? 0f : channel.Keys[channel.Keys.Count - 1].Time;
        }

        static void AddKeyTimes(List<float> times, BedrockChannel channel, float length)
        {
            for (int i = 0; i < channel.Keys.Count; i++)
            {
                times.Add(Mathf.Clamp(channel.Keys[i].Time, 0f, length));
            }
        }

        static string BuildPath(BedrockBone bone, Dictionary<string, BedrockBone> bones)
        {
            string path = bone.Name;
            string parentName = bone.Parent;
            for (int i = 0; i < 64 && !string.IsNullOrEmpty(parentName); i++)
            {
                BedrockBone parent;
                if (!bones.TryGetValue(parentName, out parent))
                {
                    break;
                }

                path = parent.Name + "/" + path;
                parentName = parent.Parent;
            }

            return path;
        }
    }
}
