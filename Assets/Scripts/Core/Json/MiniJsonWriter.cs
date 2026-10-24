using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MaidHome.Core.Json
{
    /// <summary>把 JsonValue 重新写成 JSON，主要用于原样保留 tool_calls 等扩展字段。</summary>
    public static class MiniJsonWriter
    {
        public static string Write(JsonValue value, bool indented = false)
        {
            StringBuilder builder = new StringBuilder();
            WriteValue(builder, value, indented, 0);
            return builder.ToString();
        }

        static void WriteValue(StringBuilder builder, JsonValue value, bool indented, int depth)
        {
            if (value == null || value.IsNull)
            {
                builder.Append("null");
            }
            else if (value.IsArray)
            {
                builder.Append('[');
                for (int i = 0; i < value.Count; i++)
                {
                    if (i > 0) builder.Append(',');
                    if (indented) NewLine(builder, depth + 1);
                    WriteValue(builder, value[i], indented, depth + 1);
                }

                if (indented && value.Count > 0) NewLine(builder, depth);
                builder.Append(']');
            }
            else if (value.IsObject)
            {
                builder.Append('{');
                int index = 0;
                foreach (KeyValuePair<string, JsonValue> pair in value.Fields)
                {
                    if (index++ > 0) builder.Append(',');
                    if (indented) NewLine(builder, depth + 1);
                    AppendQuoted(builder, pair.Key);
                    builder.Append(indented ? ": " : ":");
                    WriteValue(builder, pair.Value, indented, depth + 1);
                }

                if (indented && index > 0) NewLine(builder, depth);
                builder.Append('}');
            }
            else if (value.IsString)
            {
                AppendQuoted(builder, value.AsString(""));
            }
            else if (value.IsNumber)
            {
                builder.Append(value.AsDouble(0d).ToString("R", CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append(value.AsBool(false) ? "true" : "false");
            }
        }

        static void NewLine(StringBuilder builder, int depth)
        {
            builder.Append('\n');
            builder.Append(' ', depth * 2);
        }

        static void AppendQuoted(StringBuilder builder, string text)
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
                        case '\b': builder.Append("\\b"); break;
                        case '\f': builder.Append("\\f"); break;
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
