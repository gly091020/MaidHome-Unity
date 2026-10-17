using System;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 一种挂件长什么样、挂在哪儿（示众牌 / 面具 / 以后加的都在这里配）。
    /// 预制体留空就按资源名去 Resources/MaidAttachment/ 找（各 Interaction 里有默认资源名），
    /// 所以运行时 AddComponent 出来的挂件不接线也能用；想换皮就把预制体拖到对应字段上。
    /// </summary>
    [Serializable]
    public sealed class MaidAttachmentStyle
    {
        [Tooltip("挂件预制体；留空就按资源名去 Resources/MaidAttachment/ 找")]
        public GameObject Prefab;

        [Tooltip("挂点骨位名（忽略大小写/下划线/空格，按顺序取第一个找到的）；留空 = 用这个挂件的默认骨位表")]
        public string[] Bones;

        [Tooltip("挂件宽度是参考尺寸的多少倍：示众牌按女仆身高算，面具按头宽算")]
        public float SizeRatio = 0.3f;

        [Tooltip("在 SizeRatio 之上再乘一个手动倍率")]
        public float ScaleMultiplier = 1f;

        [Tooltip("位置微调：女仆的右 / 上 / 前三个方向，单位是参考尺寸的倍数")]
        public Vector3 Offset = Vector3.zero;

        [Tooltip("文字画布相对牌面的微调：女仆的右 / 上 / 前（右、前按牌宽，上按牌高）。"
            + "文字画布是焊在牌子上的，两种模型共用、不会因为模型不同而变")]
        public Vector3 TextOffset = Vector3.zero;

        [Tooltip("文字画布整体缩放：1 = 铺满牌面；字比板面大/小就调它")]
        public float TextScale = 1f;

        [Tooltip("模型朝向修正（度）。MC/Blockbench 导出的模型多半朝 -Z，女仆模型朝 +Z，所以默认转 180；挂反了改成 (0,0,0)")]
        public Vector3 FacingFix = new Vector3(0f, 180f, 0f);

        [Header("只对方块模型（SimpleBedrockModel）生效的额外修正")]
        [Tooltip("勾上以后下面几项叠加在方块模型上；不勾就是两种模型完全共用上面的设置")]
        public bool SimpleBedrockCorrection;

        [Tooltip("额外位移：女仆的右 / 上 / 前 × 参考尺寸（叠加在 Offset 上）")]
        public Vector3 SimpleBedrockOffset;

        [Tooltip("额外缩放：乘在 SizeRatio × ScaleMultiplier 上")]
        public float SimpleBedrockScale = 1f;

        public string[] BoneNames(string[] fallback)
        {
            if (Bones != null && Bones.Length > 0)
            {
                return Bones;
            }

            return fallback ?? new string[0];
        }

        public float Multiplier
        {
            get { return Mathf.Clamp(ScaleMultiplier <= 0f ? 1f : ScaleMultiplier, 0.01f, 100f); }
        }

        public float TextMultiplier
        {
            get { return Mathf.Clamp(TextScale <= 0f ? 1f : TextScale, 0.2f, 3f); }
        }

        /// <summary>这个模型要不要吃"方块模型修正"那一组</summary>
        public bool AppliesSimpleCorrection(bool simpleBedrock)
        {
            return simpleBedrock && SimpleBedrockCorrection;
        }

        public Vector3 OffsetsFor(bool simpleBedrock)
        {
            return AppliesSimpleCorrection(simpleBedrock) ? Offset + SimpleBedrockOffset : Offset;
        }

        public float MultiplierFor(bool simpleBedrock)
        {
            float value = Multiplier;
            if (AppliesSimpleCorrection(simpleBedrock))
            {
                value *= Mathf.Clamp(SimpleBedrockScale <= 0f ? 1f : SimpleBedrockScale, 0.01f, 100f);
            }

            return value;
        }

        /// <summary>预制体：接上的优先，其次 Resources/MaidAttachment/&lt;资源名&gt;</summary>
        public static GameObject ResolvePrefab(MaidAttachmentStyle style, string resourceName)
        {
            if (style != null && style.Prefab != null)
            {
                return style.Prefab;
            }

            return Resources.Load<GameObject>("MaidAttachment/" + resourceName);
        }
    }
}
