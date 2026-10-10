using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>识别结果：挑出来的物理块 + 警告。生成布娃娃、打印报告都用它。</summary>
    public sealed class MaidRagdollPlan
    {
        public Transform Root;
        public readonly List<MaidRagdollPart> Parts = new List<MaidRagdollPart>();
        public readonly List<string> Warnings = new List<string>();

        public MaidRagdollPart At(int index)
        {
            for (int i = 0; i < Parts.Count; i++)
            {
                if (Parts[i].Index == index)
                {
                    return Parts[i];
                }
            }

            return null;
        }

        /// <summary>给编辑器窗口和日志用的一页报告。</summary>
        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            int merged = 0;
            for (int i = 0; i < Parts.Count; i++)
            {
                if (Parts[i].MeshCount > 1)
                {
                    merged++;
                }
            }

            text.AppendLine("物理块 " + Parts.Count + " 块（其中 " + merged + " 块是识别到的部位，把子树里的网格合成了一体）");
            text.AppendLine();
            text.AppendLine("块   部位            骨骼                    内容            父块   尺寸(格)");
            for (int i = 0; i < Parts.Count; i++)
            {
                MaidRagdollPart part = Parts[i];
                Vector3 size = part.Size;
                string parent = part.Parent < 0 ? "根" : part.Parent.ToString();
                text.AppendLine(string.Format("#{0,-3} {1,-14} {2,-22} {3,-14} {4,-6} {5:0.000} x {6:0.000} x {7:0.000}",
                    part.Index, part.Label, part.BoneName, part.ColliderLabel, parent,
                    size.x, size.y, size.z));
            }

            for (int i = 0; i < Warnings.Count; i++)
            {
                text.AppendLine();
                text.Append("! " + Warnings[i]);
            }

            return text.ToString();
        }
    }
}
