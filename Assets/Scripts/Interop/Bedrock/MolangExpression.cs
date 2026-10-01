using System;
using System.Collections.Generic;
using System.Globalization;

namespace MaidHome.Interop.Bedrock
{
    /// <summary>Molang 求值上下文。除了动画时间，其它变量都默认 0（玩家速度、手持物之类我们暂时给不出）。</summary>
    public sealed class MolangContext
    {
        public float AnimTime;

        readonly Dictionary<string, float> _variables = new Dictionary<string, float>();

        public void Set(string name, float value)
        {
            _variables[name] = value;
        }

        public float Get(string name)
        {
            if (name == "query.anim_time" || name == "query.life_time" || name == "anim_time" || name == "life_time")
            {
                return AnimTime;
            }

            float value;
            return _variables.TryGetValue(name, out value) ? value : 0f;
        }
    }

    /// <summary>
    /// Molang 的一小部分子集：四则运算、比较、! &amp;&amp; ||、三元表达式，加上 math.* 里常用的那些函数。
    /// 认不出的函数、变量一律按 0 处理并记一条警告，不会让整个动画导不进来。
    /// </summary>
    public sealed class MolangExpression
    {
        // Molang 的三角函数吃角度，不是弧度
        const double DegreesToRadians = Math.PI / 180.0;
        const double RadiansToDegrees = 180.0 / Math.PI;

        static readonly Dictionary<string, Func<float[], float>> Functions = new Dictionary<string, Func<float[], float>>
        {
            { "math.abs", a => Math.Abs(Arg(a, 0)) },
            { "math.sin", a => (float)Math.Sin(Arg(a, 0) * DegreesToRadians) },
            { "math.cos", a => (float)Math.Cos(Arg(a, 0) * DegreesToRadians) },
            { "math.tan", a => (float)Math.Tan(Arg(a, 0) * DegreesToRadians) },
            { "math.asin", a => (float)(Math.Asin(Clamp(Arg(a, 0), -1f, 1f)) * RadiansToDegrees) },
            { "math.acos", a => (float)(Math.Acos(Clamp(Arg(a, 0), -1f, 1f)) * RadiansToDegrees) },
            { "math.atan", a => (float)(Math.Atan(Arg(a, 0)) * RadiansToDegrees) },
            { "math.atan2", a => (float)(Math.Atan2(Arg(a, 0), Arg(a, 1)) * RadiansToDegrees) },
            { "math.exp", a => (float)Math.Exp(Clamp(Arg(a, 0), -60f, 60f)) },
            { "math.sqrt", a => Math.Max(0f, Arg(a, 0)) <= 0f ? 0f : (float)Math.Sqrt(Arg(a, 0)) },
            { "math.pow", a => (float)Math.Pow(Arg(a, 0), Arg(a, 1)) },
            { "math.floor", a => (float)Math.Floor(Arg(a, 0)) },
            { "math.ceil", a => (float)Math.Ceiling(Arg(a, 0)) },
            { "math.round", a => (float)Math.Floor(Arg(a, 0) + 0.5f) },
            { "math.trunc", a => (float)Math.Truncate(Arg(a, 0)) },
            { "math.min", a => Math.Min(Arg(a, 0), Arg(a, 1)) },
            { "math.max", a => Math.Max(Arg(a, 0), Arg(a, 1)) },
            { "math.clamp", a => Clamp(Arg(a, 0), Arg(a, 1), Arg(a, 2)) },
            { "math.mod", a => Math.Abs(Arg(a, 1)) < 1e-9f ? 0f : (float)Math.IEEERemainder(Arg(a, 0), Arg(a, 1)) },
            { "math.lerp", a => Arg(a, 0) + (Arg(a, 1) - Arg(a, 0)) * Arg(a, 2) },
            { "math.hermite_blend", a => Arg(a, 0) * Arg(a, 0) * (3f - 2f * Arg(a, 0)) },
            { "math.random", a => 0f },
        };

        Func<MolangContext, float> _root;

        public static MolangExpression Compile(string text, List<string> warnings)
        {
            return Compile(text, warnings, null);
        }

        public static MolangExpression Compile(string text, List<string> warnings, List<float> animTimeRates)
        {
            MolangExpression expression = new MolangExpression();
            if (string.IsNullOrEmpty(text))
            {
                return expression;
            }

            CollectAnimTimeRates(text, animTimeRates);
            Parser parser = new Parser(text, warnings);
            expression._root = parser.Parse();
            return expression;
        }

        /// <summary>
        /// 扫出 anim_time 的乘数（形如 math.sin(x + anim_time*90)）。
        /// Molang 的三角函数吃角度，所以 anim_time*90 的周期是 360/90 = 4 秒；
        /// 这种动画没有 animation_length，只能靠这个算出该烘多长。
        /// </summary>
        public static void CollectAnimTimeRates(string text, List<float> rates)
        {
            const string token = "anim_time";
            if (rates == null || string.IsNullOrEmpty(text))
            {
                return;
            }

            int index = text.IndexOf(token, StringComparison.Ordinal);
            while (index >= 0)
            {
                int i = index + token.Length;
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                {
                    i++;
                }

                if (i < text.Length && text[i] == '*')
                {
                    i++;
                    while (i < text.Length && char.IsWhiteSpace(text[i]))
                    {
                        i++;
                    }

                    int start = i;
                    while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.'))
                    {
                        i++;
                    }

                    float rate;
                    if (i > start && float.TryParse(text.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out rate) && rate > 0f)
                    {
                        rates.Add(rate);
                    }
                }

                index = text.IndexOf(token, index + 1, StringComparison.Ordinal);
            }
        }

        public float Evaluate(MolangContext context)
        {
            return _root == null ? 0f : _root(context);
        }

        static float Arg(float[] args, int index)
        {
            return index < args.Length ? args[index] : 0f;
        }

        static float Clamp(float value, float min, float max)
        {
            if (min > max)
            {
                float swap = min;
                min = max;
                max = swap;
            }

            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        sealed class Parser
        {
            static readonly string[][] Levels =
            {
                new[] { "||" },
                new[] { "&&" },
                new[] { "==", "!=" },
                new[] { "<", "<=", ">", ">=" },
                new[] { "+", "-" },
                new[] { "*", "/", "%" },
            };

            readonly List<string> _tokens = new List<string>();
            readonly List<string> _warnings;
            readonly string _text;
            int _index;

            public Parser(string text, List<string> warnings)
            {
                _text = text;
                _warnings = warnings;
                Tokenize(text);
            }

            public Func<MolangContext, float> Parse()
            {
                Func<MolangContext, float> node = ParseTernary();
                if (_index < _tokens.Count)
                {
                    Warn("表达式里有多余的内容");
                }

                return node;
            }

            void Tokenize(string text)
            {
                int i = 0;
                while (i < text.Length)
                {
                    char c = text[i];
                    if (char.IsWhiteSpace(c))
                    {
                        i++;
                        continue;
                    }

                    if (char.IsDigit(c) || (c == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1])))
                    {
                        int start = i;
                        while (i < text.Length)
                        {
                            char d = text[i];
                            bool exponentSign = (d == '+' || d == '-') && i > start && (text[i - 1] == 'e' || text[i - 1] == 'E');
                            if (char.IsDigit(d) || d == '.' || d == 'e' || d == 'E' || exponentSign)
                            {
                                i++;
                                continue;
                            }

                            break;
                        }

                        _tokens.Add(text.Substring(start, i - start));
                        continue;
                    }

                    if (char.IsLetter(c) || c == '_')
                    {
                        int start = i;
                        while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '.'))
                        {
                            i++;
                        }

                        _tokens.Add(text.Substring(start, i - start));
                        continue;
                    }

                    if (i + 1 < text.Length)
                    {
                        string pair = text.Substring(i, 2);
                        if (pair == "==" || pair == "!=" || pair == "<=" || pair == ">=" || pair == "&&" || pair == "||")
                        {
                            _tokens.Add(pair);
                            i += 2;
                            continue;
                        }
                    }

                    _tokens.Add(c.ToString());
                    i++;
                }
            }

            string Peek()
            {
                return _index < _tokens.Count ? _tokens[_index] : null;
            }

            bool Take(string token)
            {
                if (Peek() != token)
                {
                    return false;
                }

                _index++;
                return true;
            }

            Func<MolangContext, float> ParseTernary()
            {
                Func<MolangContext, float> condition = ParseBinary(0);
                if (!Take("?"))
                {
                    return condition;
                }

                Func<MolangContext, float> yes = ParseTernary();
                Func<MolangContext, float> no;
                if (Take(":"))
                {
                    no = ParseTernary();
                }
                else
                {
                    // 从 YSM 转过来的模型里常见 "条件 ? 值" 少了 else 的写法，按 else = 0 处理
                    no = context => 0f;
                }

                Func<MolangContext, float> test = condition;
                return context => test(context) != 0f ? yes(context) : no(context);
            }

            Func<MolangContext, float> ParseBinary(int level)
            {
                if (level >= Levels.Length)
                {
                    return ParseUnary();
                }

                Func<MolangContext, float> left = ParseBinary(level + 1);
                while (true)
                {
                    string symbol = Peek();
                    if (symbol == null || Array.IndexOf(Levels[level], symbol) < 0)
                    {
                        return left;
                    }

                    _index++;
                    Func<MolangContext, float> right = ParseBinary(level + 1);
                    Func<MolangContext, float> begin = left;
                    string op = symbol;
                    left = context => ApplyBinary(op, begin(context), right(context));
                }
            }

            Func<MolangContext, float> ParseUnary()
            {
                string symbol = Peek();
                if (symbol == "-" || symbol == "+" || symbol == "!")
                {
                    _index++;
                    Func<MolangContext, float> inner = ParseUnary();
                    string op = symbol;
                    return context => ApplyUnary(op, inner(context));
                }

                return ParsePrimary();
            }

            Func<MolangContext, float> ParsePrimary()
            {
                string token = Peek();
                if (token == null)
                {
                    Warn("表达式提前结束了");
                    return context => 0f;
                }

                float number;
                if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                {
                    _index++;
                    float value = number;
                    return context => value;
                }

                if (char.IsLetter(token[0]) || token[0] == '_')
                {
                    _index++;
                    if (token == "true")
                    {
                        return context => 1f;
                    }

                    if (token == "false")
                    {
                        return context => 0f;
                    }

                    if (Take("("))
                    {
                        return ParseCall(token);
                    }

                    string variable = token;
                    return context => context.Get(variable);
                }

                if (token == "(")
                {
                    _index++;
                    Func<MolangContext, float> inner = ParseTernary();
                    if (!Take(")"))
                    {
                        Warn("少了 )");
                    }

                    return inner;
                }

                Warn("认不出的记号 " + token);
                _index++;
                return context => 0f;
            }

            Func<MolangContext, float> ParseCall(string name)
            {
                List<Func<MolangContext, float>> args = new List<Func<MolangContext, float>>();
                if (!Take(")"))
                {
                    while (true)
                    {
                        args.Add(ParseTernary());
                        if (Take(","))
                        {
                            continue;
                        }

                        if (!Take(")"))
                        {
                            Warn("函数调用少了 )");
                        }

                        break;
                    }
                }

                Func<float[], float> function;
                if (!Functions.TryGetValue(name, out function))
                {
                    Warn("不支持的函数 " + name + "，按 0 处理");
                    return context => 0f;
                }

                Func<MolangContext, float>[] captured = args.ToArray();
                return context =>
                {
                    float[] values = new float[captured.Length];
                    for (int i = 0; i < captured.Length; i++)
                    {
                        values[i] = captured[i](context);
                    }

                    return function(values);
                };
            }

            static float ApplyUnary(string op, float value)
            {
                if (op == "-")
                {
                    return -value;
                }

                if (op == "!")
                {
                    return value != 0f ? 0f : 1f;
                }

                return value;
            }

            static float ApplyBinary(string op, float left, float right)
            {
                switch (op)
                {
                    case "+":
                        return left + right;
                    case "-":
                        return left - right;
                    case "*":
                        return left * right;
                    case "/":
                        return Math.Abs(right) < 1e-12f ? 0f : left / right;
                    case "%":
                        return Math.Abs(right) < 1e-12f ? 0f : (float)Math.IEEERemainder(left, right);
                    case "==":
                        return Math.Abs(left - right) < 1e-9f ? 1f : 0f;
                    case "!=":
                        return Math.Abs(left - right) < 1e-9f ? 0f : 1f;
                    case "<":
                        return left < right ? 1f : 0f;
                    case "<=":
                        return left <= right ? 1f : 0f;
                    case ">":
                        return left > right ? 1f : 0f;
                    case ">=":
                        return left >= right ? 1f : 0f;
                    case "&&":
                        return left != 0f && right != 0f ? 1f : 0f;
                    default:
                        return left != 0f || right != 0f ? 1f : 0f;
                }
            }

            void Warn(string reason)
            {
                string text = "Molang 解析问题（" + reason + "）: " + _text;
                if (!_warnings.Contains(text))
                {
                    _warnings.Add(text);
                }
            }
        }
    }
}
