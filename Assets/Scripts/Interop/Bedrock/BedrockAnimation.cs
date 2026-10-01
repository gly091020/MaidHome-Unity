using System.Collections.Generic;
using System.Globalization;
using MaidHome.Core.Json;
using UnityEngine;

namespace MaidHome.Interop.Bedrock
{
    public enum BedrockAnimationLoop
    {
        /// <summary>播完停在最后一帧（文件里没写 loop）</summary>
        Once,
        Loop,
        /// <summary>loop = "hold_on_last_frame"</summary>
        HoldOnLastFrame,
    }

    public sealed class BedrockAnimationBone
    {
        public string Name;
        public readonly BedrockChannel Rotation = new BedrockChannel();
        public readonly BedrockChannel Position = new BedrockChannel();
        public readonly BedrockChannel Scale = new BedrockChannel();
    }

    public sealed class BedrockAnimation
    {
        public string Name = "";
        public float Length;
        /// <summary>文件里没写 animation_length，长度是按最后一个关键帧推出来的</summary>
        public bool LengthFromKeys;
        /// <summary>既没写长度、也没有关键帧：TLM 里长度当成无限，靠 anim_time 的 Molang 表达式驱动</summary>
        public bool Unbounded;
        /// <summary>Unbounded 且被 anim_time 驱动时估出来的循环周期（秒），0 表示这条动画是纯静止姿势</summary>
        public float ProceduralPeriod;
        public BedrockAnimationLoop Loop = BedrockAnimationLoop.Once;
        public readonly List<BedrockAnimationBone> Bones = new List<BedrockAnimationBone>();

        public BedrockAnimationBone FindBone(string name)
        {
            for (int i = 0; i < Bones.Count; i++)
            {
                if (Bones[i].Name == name)
                {
                    return Bones[i];
                }
            }

            return null;
        }
    }

    public sealed class BedrockAnimationSet
    {
        public string FormatVersion = "?";
        public readonly List<BedrockAnimation> Animations = new List<BedrockAnimation>();
        public readonly List<string> Warnings = new List<string>();

        public BedrockAnimation Find(string name)
        {
            for (int i = 0; i < Animations.Count; i++)
            {
                if (Animations[i].Name == name)
                {
                    return Animations[i];
                }
            }

            return null;
        }

        public static BedrockAnimationSet ParseFile(string path)
        {
            return Parse(MiniJson.ParseFile(path));
        }

        public static BedrockAnimationSet Parse(string json)
        {
            return Parse(MiniJson.Parse(json));
        }

        public static BedrockAnimationSet Parse(JsonValue root)
        {
            BedrockAnimationSet set = new BedrockAnimationSet();
            set.FormatVersion = root["format_version"].ToString();

            JsonValue animations = root["animations"];
            if (animations.IsNull)
            {
                set.Warnings.Add("文件里没有 animations 对象");
                return set;
            }

            foreach (KeyValuePair<string, JsonValue> pair in animations.Fields)
            {
                BedrockAnimation animation = new BedrockAnimation();
                animation.Name = pair.Key;
                ReadAnimation(pair.Value, animation, set.Warnings);
                set.Animations.Add(animation);
            }

            return set;
        }

        static void ReadAnimation(JsonValue node, BedrockAnimation animation, List<string> warnings)
        {
            JsonValue length = node["animation_length"];
            if (length.IsNumber)
            {
                animation.Length = length.AsFloat(0f);
            }
            else
            {
                animation.LengthFromKeys = true;
            }

            JsonValue loop = node["loop"];
            if (loop.IsString && loop.AsString("") == "hold_on_last_frame")
            {
                animation.Loop = BedrockAnimationLoop.HoldOnLastFrame;
            }
            else if (loop.IsNull ? false : loop.AsBool(false))
            {
                animation.Loop = BedrockAnimationLoop.Loop;
            }

            JsonValue bones = node["bones"];
            if (bones.IsNull)
            {
                warnings.Add("动画 " + animation.Name + " 没有 bones");
                return;
            }

            float longest = 0f;
            List<float> animTimeRates = new List<float>();
            foreach (KeyValuePair<string, JsonValue> pair in bones.Fields)
            {
                BedrockAnimationBone bone = new BedrockAnimationBone();
                bone.Name = pair.Key;
                ReadChannel(pair.Value["rotation"], bone.Rotation, warnings, 0f, animTimeRates);
                ReadChannel(pair.Value["position"], bone.Position, warnings, 0f, animTimeRates);
                ReadChannel(pair.Value["scale"], bone.Scale, warnings, 1f, animTimeRates);
                animation.Bones.Add(bone);
                longest = Mathf.Max(longest, LastKeyTime(bone));
            }

            if (animation.LengthFromKeys)
            {
                animation.Length = longest;
                if (longest <= 0f)
                {
                    animation.Unbounded = true;
                    animation.ProceduralPeriod = EstimatePeriod(animTimeRates, animation.Name, warnings);
                }
            }

            if (animation.Length <= 0f && !animation.LengthFromKeys)
            {
                warnings.Add("动画 " + animation.Name + " 的 animation_length 是 0");
            }
        }

        /// <summary>
        /// 估算 anim_time 驱动动画的循环周期：周期 = 360/乘数秒，取所有乘数周期的最小公倍数（上限 60 秒）。
        /// 找不到就返回 0，调用方按「静止姿势」处理。
        /// </summary>
        static float EstimatePeriod(List<float> rates, string animationName, List<string> warnings)
        {
            if (rates.Count == 0)
            {
                return 0f;
            }

            float longest = 0f;
            for (int i = 0; i < rates.Count; i++)
            {
                float period = 360f / rates[i];
                if (period > longest)
                {
                    longest = period;
                }
            }

            if (longest <= 0f)
            {
                return 0f;
            }

            for (int multiple = 1; multiple <= 60; multiple++)
            {
                float candidate = longest * multiple;
                if (candidate > 60f)
                {
                    break;
                }

                bool fits = true;
                for (int i = 0; i < rates.Count; i++)
                {
                    float ratio = candidate * rates[i] / 360f;
                    if (Mathf.Abs(ratio - Mathf.Round(ratio)) > 1e-3f)
                    {
                        fits = false;
                        break;
                    }
                }

                if (fits)
                {
                    return candidate;
                }
            }

            warnings.Add("动画 " + animationName + " 的 anim_time 周期凑不出公倍数，按 " + longest + " 秒循环");
            return longest;
        }

        static float LastKeyTime(BedrockAnimationBone bone)
        {
            float longest = 0f;
            longest = Mathf.Max(longest, LastKeyTime(bone.Rotation));
            longest = Mathf.Max(longest, LastKeyTime(bone.Position));
            return Mathf.Max(longest, LastKeyTime(bone.Scale));
        }

        static float LastKeyTime(BedrockChannel channel)
        {
            return channel.Keys.Count == 0 ? 0f : channel.Keys[channel.Keys.Count - 1].Time;
        }

        static void ReadChannel(JsonValue node, BedrockChannel channel, List<string> warnings, float fill, List<float> animTimeRates)
        {
            if (node.IsNull)
            {
                return;
            }

            channel.Present = true;
            if (!node.IsObject)
            {
                channel.Constant = ReadValue3(node, fill, warnings, animTimeRates);
                return;
            }

            // 对象就是关键帧表，键是秒
            foreach (KeyValuePair<string, JsonValue> pair in node.Fields)
            {
                float time;
                if (!float.TryParse(pair.Key, NumberStyles.Float, CultureInfo.InvariantCulture, out time))
                {
                    warnings.Add("时间戳不是数字，已跳过: " + pair.Key);
                    continue;
                }

                BedrockKeyframe key = new BedrockKeyframe();
                key.Time = time;
                JsonValue item = pair.Value;
                if (item.IsObject && !item["pre"].IsNull && !item["post"].IsNull)
                {
                    key.Pre = ReadValue3(item["pre"], fill, warnings, animTimeRates);
                    key.Post = ReadValue3(item["post"], fill, warnings, animTimeRates);
                }
                else if (item.IsObject && !item["vector"].IsNull)
                {
                    key.Pre = ReadValue3(item["vector"], fill, warnings, animTimeRates);
                    key.Post = key.Pre;
                }
                else if (item.IsObject)
                {
                    JsonValue single = item["pre"].IsNull ? item["post"] : item["pre"];
                    key.Pre = ReadValue3(single, fill, warnings, animTimeRates);
                    key.Post = key.Pre;
                }
                else
                {
                    key.Pre = ReadValue3(item, fill, warnings, animTimeRates);
                    key.Post = key.Pre;
                }

                if (item.IsObject)
                {
                    // 老的 Blockbench 用 easing，新的用 lerp_mode
                    key.CatmullRom = item["lerp_mode"].AsString("") == "catmullrom"
                        || item["easing"].AsString("") == "catmullrom";
                }

                channel.Keys.Add(key);
            }

            if (channel.Keys.Count > 0)
            {
                channel.Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
                channel.Constant = channel.Keys[0].Pre;
            }
        }

        static BedrockValue3 ReadValue3(JsonValue node, float fill, List<string> warnings, List<float> animTimeRates)
        {
            BedrockValue3 value = BedrockValue3.Fill(fill);
            if (node.IsNumber)
            {
                return BedrockValue3.Fill(node.AsFloat(fill));
            }

            if (node.IsString)
            {
                MolangExpression expression = MolangExpression.Compile(node.AsString(""), warnings, animTimeRates);
                value.XExpression = expression;
                value.YExpression = expression;
                value.ZExpression = expression;
                return value;
            }

            if (!node.IsArray)
            {
                return value;
            }

            if (node.Count >= 3)
            {
                value.X = ReadComponent(node[0], fill, warnings, animTimeRates, out value.XExpression);
                value.Y = ReadComponent(node[1], fill, warnings, animTimeRates, out value.YExpression);
                value.Z = ReadComponent(node[2], fill, warnings, animTimeRates, out value.ZExpression);
                return value;
            }

            if (node.Count == 1)
            {
                return ReadValue3(node[0], fill, warnings, animTimeRates);
            }

            return value;
        }

        static float ReadComponent(JsonValue node, float fill, List<string> warnings, List<float> animTimeRates, out MolangExpression expression)
        {
            expression = null;
            if (node.IsString)
            {
                expression = MolangExpression.Compile(node.AsString(""), warnings, animTimeRates);
                return fill;
            }

            return node.AsFloat(fill);
        }
    }
}
