using System.Collections.Generic;
using System.Text;
using MaidHome.Core.Json;

namespace MaidHome.Interop.Maid
{
    /// <summary>
    /// 女仆身上挂的「游街示众」牌子（照 Love-And-Loathe 的 MaidSignData）：
    /// 四行文字 + 一个染料色 + 要不要吓到旁观的别的女仆。
    /// 存在女仆自己的文件夹里，读写走 MaidAttachmentStore。
    /// </summary>
    public sealed class MaidSignData
    {
        public const int MaxLines = 4;
        /// <summary>手打文本时单行的上限，防超长字符串</summary>
        public const int MaxLineLength = 64;

        public bool Enabled;
        public readonly string[] Lines = new string[MaxLines];
        /// <summary>染料色名（white / orange / …），渲染时查 MaidSignPalette；存档里存名字，认不出按白色</summary>
        public string Color = "white";
        /// <summary>false 时这块牌子只作展示，不威慑旁边看到它的女仆</summary>
        public bool Scare = true;

        /// <summary>四行拼起来的纯文本（空行跳过），空字符串表示牌子没内容</summary>
        public string Text
        {
            get
            {
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < MaxLines; i++)
                {
                    string line = Lines[i];
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (builder.Length > 0)
                    {
                        builder.Append('\n');
                    }

                    builder.Append(line.Trim());
                }

                return builder.ToString();
            }
        }

        public bool IsBlank
        {
            get { return Text.Length == 0; }
        }

        public MaidSignData Clone()
        {
            MaidSignData copy = new MaidSignData();
            copy.Enabled = Enabled;
            copy.Color = Color;
            copy.Scare = Scare;
            for (int i = 0; i < MaxLines; i++)
            {
                copy.Lines[i] = Lines[i];
            }

            return copy;
        }

        /// <summary>取下牌子（文字、颜色都清掉，威慑开关留着）</summary>
        public void Clear()
        {
            Enabled = false;
            for (int i = 0; i < MaxLines; i++)
            {
                Lines[i] = "";
            }
        }

        /// <summary>
        /// 存档解析：坏的、缺的字段都退回默认值（牌子挂不上总比读档失败强）。
        /// </summary>
        public static MaidSignData Parse(JsonValue value)
        {
            MaidSignData data = new MaidSignData();
            if (value == null || !value.IsObject)
            {
                return data;
            }

            data.Enabled = value["enabled"].AsBool(false);
            data.Color = MaidSignColorName(value["color"].AsString("white"));
            data.Scare = value["scare"].AsBool(true);

            JsonValue lines = value["lines"];
            for (int i = 0; i < MaxLines; i++)
            {
                string line = lines[i].AsString("");
                if (line.Length > MaxLineLength)
                {
                    line = line.Substring(0, MaxLineLength);
                }

                data.Lines[i] = line;
            }

            return data;
        }

        public JsonValue ToJson()
        {
            List<JsonValue> lines = new List<JsonValue>(MaxLines);
            for (int i = 0; i < MaxLines; i++)
            {
                string line = Lines[i] ?? "";
                if (line.Length > MaxLineLength)
                {
                    line = line.Substring(0, MaxLineLength);
                }

                lines.Add(JsonValue.FromString(line));
            }

            Dictionary<string, JsonValue> fields = new Dictionary<string, JsonValue>();
            fields["enabled"] = JsonValue.FromBool(Enabled);
            fields["lines"] = JsonValue.FromArray(lines);
            fields["color"] = JsonValue.FromString(string.IsNullOrEmpty(Color) ? "white" : Color);
            fields["scare"] = JsonValue.FromBool(Scare);
            return JsonValue.FromObject(fields);
        }

        static string MaidSignColorName(string value)
        {
            // 色名统一小写存，渲染和色板那边再按名字查（认不出的名字会落到白色）
            return string.IsNullOrEmpty(value) ? "white" : value.Trim().ToLowerInvariant();
        }
    }
}
