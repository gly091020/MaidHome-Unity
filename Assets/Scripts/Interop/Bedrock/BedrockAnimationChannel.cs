using System.Collections.Generic;
using UnityEngine;

namespace MaidHome.Interop.Bedrock
{
    /// <summary>动画里的一个三元组数值。某一轴带 MolangExpression 时用表达式求值，否则用常量。</summary>
    public struct BedrockValue3
    {
        public float X;
        public float Y;
        public float Z;
        public MolangExpression XExpression;
        public MolangExpression YExpression;
        public MolangExpression ZExpression;

        public static BedrockValue3 Fill(float value)
        {
            BedrockValue3 result = new BedrockValue3();
            result.X = value;
            result.Y = value;
            result.Z = value;
            return result;
        }

        public Vector3 Evaluate(MolangContext context)
        {
            return new Vector3(
                XExpression == null ? X : XExpression.Evaluate(context),
                YExpression == null ? Y : YExpression.Evaluate(context),
                ZExpression == null ? Z : ZExpression.Evaluate(context));
        }
    }

    /// <summary>
    /// 一个关键帧。Pre 是从上一段插值过来的落点，Post 是离开这一帧时的值，
    /// 两者不同就是 bedrock 里的突变帧（平时相等）。
    /// </summary>
    public sealed class BedrockKeyframe
    {
        public float Time;
        public BedrockValue3 Pre;
        public BedrockValue3 Post;
        public bool CatmullRom;
    }

    /// <summary>
    /// 一个骨骼的一个通道（rotation / position / scale）。
    /// 采样规则照抄 TLM 的 geckolib3：段 [i, i+1] 用第 i 帧的插值方式，offset 帧如果是 catmullrom 也按 catmullrom。
    /// </summary>
    public sealed class BedrockChannel
    {
        public bool Present;
        public BedrockValue3 Constant;
        public readonly List<BedrockKeyframe> Keys = new List<BedrockKeyframe>();

        public Vector3 Sample(float time, MolangContext context)
        {
            if (Keys.Count == 0)
            {
                return Constant.Evaluate(context);
            }

            if (Keys.Count == 1 || time <= Keys[0].Time)
            {
                return Keys[0].Pre.Evaluate(context);
            }

            int last = Keys.Count - 1;
            if (time >= Keys[last].Time)
            {
                return Keys[last].Post.Evaluate(context);
            }

            int index = 0;
            for (int i = 0; i < last; i++)
            {
                if (Keys[i].Time <= time && time < Keys[i + 1].Time)
                {
                    index = i;
                    break;
                }
            }

            BedrockKeyframe begin = Keys[index];
            BedrockKeyframe end = Keys[index + 1];
            float span = end.Time - begin.Time;
            float percent = span <= 1e-9f ? 0f : (time - begin.Time) / span;
            Vector3 start = begin.Post.Evaluate(context);
            Vector3 stop = end.Pre.Evaluate(context);
            bool catmullRom = end.CatmullRom || begin.CatmullRom;
            if (!catmullRom)
            {
                return start + (stop - start) * percent;
            }

            Vector3 left = Keys[Mathf.Max(0, index - 1)].Post.Evaluate(context);
            Vector3 right = Keys[Mathf.Min(last, index + 2)].Pre.Evaluate(context);
            return new Vector3(
                CatmullRom(percent, left.x, start.x, stop.x, right.x),
                CatmullRom(percent, left.y, start.y, stop.y, right.y),
                CatmullRom(percent, left.z, start.z, stop.z, right.z));
        }

        static float CatmullRom(float percent, float left, float begin, float end, float right)
        {
            double v0 = (end - left) * 0.5;
            double v1 = (right - begin) * 0.5;
            double t2 = (double)percent * percent;
            double t3 = t2 * percent;
            return (float)((2 * begin - 2 * end + v0 + v1) * t3
                + (-3 * begin + 3 * end - 2 * v0 - v1) * t2
                + v0 * percent + begin);
        }
    }
}
