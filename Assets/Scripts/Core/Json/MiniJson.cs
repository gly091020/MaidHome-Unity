using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MaidHome.Core.Json
{
    public enum JsonKind
    {
        Null,
        Bool,
        Number,
        String,
        Array,
        Object,
    }

    public sealed class JsonValue
    {
        static readonly JsonValue NullValue = new JsonValue();

        readonly JsonKind _kind;
        readonly bool _bool;
        readonly double _number;
        readonly string _text;
        readonly List<JsonValue> _array;
        readonly Dictionary<string, JsonValue> _fields;

        JsonValue()
        {
            _kind = JsonKind.Null;
        }

        JsonValue(bool value)
        {
            _kind = JsonKind.Bool;
            _bool = value;
        }

        JsonValue(double value)
        {
            _kind = JsonKind.Number;
            _number = value;
        }

        JsonValue(string value)
        {
            _kind = JsonKind.String;
            _text = value;
        }

        JsonValue(List<JsonValue> items)
        {
            _kind = JsonKind.Array;
            _array = items;
        }

        JsonValue(Dictionary<string, JsonValue> fields)
        {
            _kind = JsonKind.Object;
            _fields = fields;
        }

        internal static JsonValue Null
        {
            get { return NullValue; }
        }

        internal static JsonValue FromBool(bool value)
        {
            return new JsonValue(value);
        }

        internal static JsonValue FromNumber(double value)
        {
            return new JsonValue(value);
        }

        internal static JsonValue FromString(string value)
        {
            return new JsonValue(value);
        }

        internal static JsonValue FromArray(List<JsonValue> items)
        {
            return new JsonValue(items);
        }

        internal static JsonValue FromObject(Dictionary<string, JsonValue> fields)
        {
            return new JsonValue(fields);
        }

        public JsonKind Kind
        {
            get { return _kind; }
        }

        public bool IsNull
        {
            get { return _kind == JsonKind.Null; }
        }

        public bool IsArray
        {
            get { return _kind == JsonKind.Array; }
        }

        public bool IsObject
        {
            get { return _kind == JsonKind.Object; }
        }

        public bool IsNumber
        {
            get { return _kind == JsonKind.Number; }
        }

        public bool IsString
        {
            get { return _kind == JsonKind.String; }
        }

        public int Count
        {
            get
            {
                if (_kind == JsonKind.Array)
                {
                    return _array.Count;
                }

                return _kind == JsonKind.Object ? _fields.Count : 0;
            }
        }

        public IEnumerable<KeyValuePair<string, JsonValue>> Fields
        {
            get
            {
                if (_kind != JsonKind.Object)
                {
                    yield break;
                }

                foreach (KeyValuePair<string, JsonValue> pair in _fields)
                {
                    yield return pair;
                }
            }
        }

        public JsonValue this[int index]
        {
            get
            {
                if (_kind != JsonKind.Array || index < 0 || index >= _array.Count)
                {
                    return NullValue;
                }

                return _array[index];
            }
        }

        public JsonValue this[string key]
        {
            get
            {
                JsonValue value;
                if (_kind == JsonKind.Object && _fields.TryGetValue(key, out value))
                {
                    return value;
                }

                return NullValue;
            }
        }

        public bool Has(string key)
        {
            return _kind == JsonKind.Object && _fields.ContainsKey(key);
        }

        public string AsString(string fallback)
        {
            return _kind == JsonKind.String ? _text : fallback;
        }

        public float AsFloat(float fallback)
        {
            if (_kind == JsonKind.Number)
            {
                return (float)_number;
            }

            if (_kind == JsonKind.String && float.TryParse(_text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            {
                return parsed;
            }

            return fallback;
        }

        public int AsInt(int fallback)
        {
            if (_kind == JsonKind.Number)
            {
                return (int)Math.Round(_number);
            }

            if (_kind == JsonKind.String && int.TryParse(_text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                return parsed;
            }

            return fallback;
        }

        public bool AsBool(bool fallback)
        {
            if (_kind == JsonKind.Bool)
            {
                return _bool;
            }

            if (_kind == JsonKind.Number)
            {
                return Math.Abs(_number) > double.Epsilon;
            }

            if (_kind == JsonKind.String && bool.TryParse(_text, out bool parsed))
            {
                return parsed;
            }

            return fallback;
        }

        public override string ToString()
        {
            switch (_kind)
            {
                case JsonKind.Null:
                    return "null";
                case JsonKind.Bool:
                    return _bool ? "true" : "false";
                case JsonKind.Number:
                    return _number.ToString(CultureInfo.InvariantCulture);
                case JsonKind.String:
                    return _text;
                default:
                    return _kind == JsonKind.Array ? "[array:" + _array.Count + "]" : "{object:" + _fields.Count + "}";
            }
        }
    }

    public sealed class JsonParseException : Exception
    {
        public JsonParseException(string message, int position)
            : base(message + " (字符位置 " + position + ")")
        {
            Position = position;
        }

        public int Position { get; private set; }
    }

    public static class MiniJson
    {
        public static JsonValue ParseFile(string path)
        {
            return Parse(File.ReadAllText(path, Encoding.UTF8));
        }

        public static JsonValue Parse(string text)
        {
            if (text == null)
            {
                throw new JsonParseException("文本为空", 0);
            }

            int index = 0;
            JsonValue value = ParseValue(text, ref index);
            SkipWhitespace(text, ref index);
            if (index < text.Length)
            {
                throw new JsonParseException("结尾有多余内容", index);
            }

            return value;
        }

        static JsonValue ParseValue(string text, ref int index)
        {
            SkipWhitespace(text, ref index);
            if (index >= text.Length)
            {
                throw new JsonParseException("内容意外结束", index);
            }

            char c = text[index];
            switch (c)
            {
                case '{':
                    return ParseObject(text, ref index);
                case '[':
                    return ParseArray(text, ref index);
                case '"':
                    return JsonValue.FromString(ParseString(text, ref index));
                case 't':
                    Expect(text, ref index, "true");
                    return JsonValue.FromBool(true);
                case 'f':
                    Expect(text, ref index, "false");
                    return JsonValue.FromBool(false);
                case 'n':
                    Expect(text, ref index, "null");
                    return JsonValue.Null;
                default:
                    return JsonValue.FromNumber(ParseNumber(text, ref index));
            }
        }

        static JsonValue ParseObject(string text, ref int index)
        {
            Dictionary<string, JsonValue> fields = new Dictionary<string, JsonValue>();
            index++;
            SkipWhitespace(text, ref index);
            if (index < text.Length && text[index] == '}')
            {
                index++;
                return JsonValue.FromObject(fields);
            }

            while (true)
            {
                SkipWhitespace(text, ref index);
                if (index >= text.Length || text[index] != '"')
                {
                    throw new JsonParseException("对象键必须是字符串", index);
                }

                string key = ParseString(text, ref index);
                SkipWhitespace(text, ref index);
                if (index >= text.Length || text[index] != ':')
                {
                    throw new JsonParseException("键后面缺少冒号", index);
                }

                index++;
                fields[key] = ParseValue(text, ref index);
                SkipWhitespace(text, ref index);
                if (index >= text.Length)
                {
                    throw new JsonParseException("对象没有闭合", index);
                }

                if (text[index] == ',')
                {
                    index++;
                    continue;
                }

                if (text[index] == '}')
                {
                    index++;
                    return JsonValue.FromObject(fields);
                }

                throw new JsonParseException("对象里出现了意外字符", index);
            }
        }

        static JsonValue ParseArray(string text, ref int index)
        {
            List<JsonValue> items = new List<JsonValue>();
            index++;
            SkipWhitespace(text, ref index);
            if (index < text.Length && text[index] == ']')
            {
                index++;
                return JsonValue.FromArray(items);
            }

            while (true)
            {
                items.Add(ParseValue(text, ref index));
                SkipWhitespace(text, ref index);
                if (index >= text.Length)
                {
                    throw new JsonParseException("数组没有闭合", index);
                }

                if (text[index] == ',')
                {
                    index++;
                    continue;
                }

                if (text[index] == ']')
                {
                    index++;
                    return JsonValue.FromArray(items);
                }

                throw new JsonParseException("数组里出现了意外字符", index);
            }
        }

        static string ParseString(string text, ref int index)
        {
            index++;
            StringBuilder builder = new StringBuilder();
            while (true)
            {
                if (index >= text.Length)
                {
                    throw new JsonParseException("字符串没有闭合", index);
                }

                char c = text[index++];
                if (c == '"')
                {
                    return builder.ToString();
                }

                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (index >= text.Length)
                {
                    throw new JsonParseException("转义符后面没有内容", index);
                }

                char escape = text[index++];
                switch (escape)
                {
                    case '"':
                        builder.Append('"');
                        break;
                    case '\\':
                        builder.Append('\\');
                        break;
                    case '/':
                        builder.Append('/');
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u':
                        if (index + 4 > text.Length)
                        {
                            throw new JsonParseException("\\u 转义不完整", index);
                        }

                        string hex = text.Substring(index, 4);
                        if (!ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort code))
                        {
                            throw new JsonParseException("\\u 转义不是合法的十六进制", index);
                        }

                        builder.Append((char)code);
                        index += 4;
                        break;
                    default:
                        throw new JsonParseException("不认识的转义符 \\" + escape, index);
                }
            }
        }

        static double ParseNumber(string text, ref int index)
        {
            int start = index;
            if (index < text.Length && (text[index] == '-' || text[index] == '+'))
            {
                index++;
            }

            while (index < text.Length && (char.IsDigit(text[index]) || text[index] == '.' || text[index] == 'e' || text[index] == 'E' || text[index] == '+' || text[index] == '-'))
            {
                index++;
            }

            string slice = text.Substring(start, index - start);
            if (!double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                throw new JsonParseException("不是合法的数字: " + slice, start);
            }

            return value;
        }

        static void Expect(string text, ref int index, string literal)
        {
            if (index + literal.Length > text.Length || string.CompareOrdinal(text, index, literal, 0, literal.Length) != 0)
            {
                throw new JsonParseException("期望 " + literal, index);
            }

            index += literal.Length;
        }

        static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length)
            {
                char c = text[index];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                {
                    index++;
                    continue;
                }

                if (c == '\uFEFF')
                {
                    index++;
                    continue;
                }

                break;
            }
        }
    }
}
