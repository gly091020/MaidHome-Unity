using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MaidHome.Core.Json;
using UnityEngine;

namespace MaidHome.Interop.Maid
{
    /// <summary>
    /// 女仆身上挂的东西：saves/maid/&lt;uuid&gt;/attachments.json。
    /// 单独一个文件，不塞进 maid.json —— 那份 MC 侧也在写，Unity 一存就把不认识的字段吃掉了。
    /// 现在只有「示众牌」一种挂件，以后加别的就往这里加字段；不认识的字段会原样保留再写回去。
    /// </summary>
    public sealed class MaidAttachmentStore
    {
        public const int Version = 1;
        public const string FileName = "attachments.json";
        const string SignKey = "sign";
        const string MaskKey = "mask";

        public readonly MaidSignData Sign = new MaidSignData();
        public readonly MaidMaskData Mask = new MaidMaskData();

        string _path;

        /// <summary>这个版本不认识的挂件（原样保留，别把以后加的东西写没了）</summary>
        readonly Dictionary<string, JsonValue> _others = new Dictionary<string, JsonValue>();

        public static string PathFor(MaidSaveData maid)
        {
            if (maid == null || string.IsNullOrEmpty(maid.Folder))
            {
                return "";
            }

            return Path.Combine(maid.Folder, FileName);
        }

        /// <summary>读这只女仆的挂件；文件不在、坏了都退回空的一份（不影响她正常出现）。</summary>
        public static MaidAttachmentStore Load(MaidSaveData maid)
        {
            MaidAttachmentStore store = new MaidAttachmentStore();
            string path = PathFor(maid);
            store._path = path;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return store;
            }

            try
            {
                JsonValue root = MiniJson.Parse(File.ReadAllText(path, Encoding.UTF8));
                if (!root.IsObject)
                {
                    return store;
                }

                foreach (KeyValuePair<string, JsonValue> field in root.Fields)
                {
                    if (field.Key == SignKey)
                    {
                        MaidSignData parsed = MaidSignData.Parse(field.Value);
                        store.Sign.Enabled = parsed.Enabled;
                        store.Sign.Color = parsed.Color;
                        store.Sign.Scare = parsed.Scare;
                        for (int i = 0; i < MaidSignData.MaxLines; i++)
                        {
                            store.Sign.Lines[i] = parsed.Lines[i];
                        }
                    }
                    else if (field.Key == MaskKey)
                    {
                        MaidMaskData parsed = MaidMaskData.Parse(field.Value);
                        store.Mask.Enabled = parsed.Enabled;
                        store.Mask.Photo = parsed.Photo;
                    }
                    else
                    {
                        store._others[field.Key] = field.Value;
                    }
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning("[挂件] " + FileName + " 读不出来，按没挂东西处理：" + error.Message
                    + "（" + path + "）");
            }

            return store;
        }

        /// <summary>写回 attachments.json：先写 .tmp 再替换，原文件留一份 .bak。</summary>
        public bool Save(out string error)
        {
            error = "";
            if (string.IsNullOrEmpty(_path))
            {
                error = "不知道要写到哪个文件夹";
                return false;
            }

            string temp = _path + ".tmp";
            try
            {
                string folder = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                File.WriteAllText(temp, ToJson(), new UTF8Encoding(false));
                if (File.Exists(_path))
                {
                    File.Copy(_path, _path + ".bak", true);
                    File.Delete(_path);
                }

                File.Move(temp, _path);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public string ToJson()
        {
            Dictionary<string, JsonValue> fields = new Dictionary<string, JsonValue>();
            fields["version"] = JsonValue.FromNumber(Version);
            fields[SignKey] = Sign.ToJson();
            fields[MaskKey] = Mask.ToJson();
            foreach (KeyValuePair<string, JsonValue> other in _others)
            {
                if (other.Key == "version" || other.Key == SignKey || other.Key == MaskKey)
                {
                    continue;
                }

                fields[other.Key] = other.Value;
            }

            StringBuilder builder = new StringBuilder();
            WriteJson(JsonValue.FromObject(fields), builder, 0);
            builder.Append('\n');
            return builder.ToString();
        }

        // ------------------------------------------------------------ 序列化

        // MiniJson 只读不写，这里补一个极小的写出（只为了把 JsonValue 原样存回文件）
        static void WriteJson(JsonValue value, StringBuilder builder, int depth)
        {
            switch (value.Kind)
            {
                case JsonKind.Null:
                    builder.Append("null");
                    break;
                case JsonKind.Bool:
                case JsonKind.Number:
                    builder.Append(value.ToString());
                    break;
                case JsonKind.String:
                    Quote(value.ToString(), builder);
                    break;
                case JsonKind.Array:
                    WriteArray(value, builder, depth);
                    break;
                default:
                    WriteObject(value, builder, depth);
                    break;
            }
        }

        static void WriteArray(JsonValue value, StringBuilder builder, int depth)
        {
            if (value.Count == 0)
            {
                builder.Append("[]");
                return;
            }

            string inner = Indent(depth + 1);
            string outer = Indent(depth);
            builder.Append('[');
            for (int i = 0; i < value.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append('\n').Append(inner);
                WriteJson(value[i], builder, depth + 1);
            }

            builder.Append('\n').Append(outer).Append(']');
        }

        static void WriteObject(JsonValue value, StringBuilder builder, int depth)
        {
            if (value.Count == 0)
            {
                builder.Append("{}");
                return;
            }

            string inner = Indent(depth + 1);
            string outer = Indent(depth);
            builder.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, JsonValue> field in value.Fields)
            {
                if (!first)
                {
                    builder.Append(',');
                }

                first = false;
                builder.Append('\n').Append(inner);
                Quote(field.Key, builder);
                builder.Append(": ");
                WriteJson(field.Value, builder, depth + 1);
            }

            builder.Append('\n').Append(outer).Append('}');
        }

        static string Indent(int depth)
        {
            return new string(' ', depth * 2);
        }

        static void Quote(string text, StringBuilder builder)
        {
            builder.Append('"');
            if (text != null)
            {
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    switch (c)
                    {
                        case '"': builder.Append("\\\""); break;
                        case '\\': builder.Append("\\\\"); break;
                        case '\n': builder.Append("\\n"); break;
                        case '\r': builder.Append("\\r"); break;
                        case '\t': builder.Append("\\t"); break;
                        default:
                            if (c < ' ')
                            {
                                builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                builder.Append(c);
                            }

                            break;
                    }
                }
            }

            builder.Append('"');
        }
    }
}
