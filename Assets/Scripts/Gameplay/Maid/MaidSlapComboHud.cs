using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 扇耳光的连击显示，照抄 moreanimation 的 SlapComboHud / SlapComboState / SlapMilestoneState：
    /// 顶部居中的「连击 ×N」彩虹字，每加一下有个弹一下的脉冲，超时前 0.35 秒淡出；
    /// 每 100 连放一次「N连抽」大字（用模组那张 256x64 的位图字体，自带描边和弹出动画），
    /// 同时两侧各 64 个彩色粒子往外飞、两边贴彩虹光带，持续 1.2 秒。
    /// 画在 OnGUI 里，不建场景物体；状态全在这个类里，MaidFaceInteraction 只管调 Confirm。
    /// </summary>
    public sealed class MaidSlapComboHud
    {
        // 模组 SlapComboHud 里的常量
        const float FadeSeconds = 0.35f;
        const float PulseSeconds = 0.35f;
        const float EffectSeconds = 1.2f;
        const float TextSeconds = 1.8f;
        const int ParticlesPerSide = 64;
        const float HueCycleSeconds = 2.4f;
        const float ComboHueStep = 0.09f;

        // 位图字体：assets/moreanimation/font/combo_display.json（256x64，8 列 2 行）
        static readonly string[] GlyphChars = { "01234567", "89SLAP连抽" };
        const int GlyphColumns = 8;

        int _count;
        float _lastSlapAt = -1000f;
        int _milestone;
        int _lastAward;
        float _milestoneAt = -1000f;

        Font _font;
        GUIStyle _style;
        readonly GUIContent _measure = new GUIContent();
        Texture2D _white;
        Texture2D _comboFont;
        bool _comboFontTried;

        /// <summary>多久没再扇就算断连击，由 MaidFaceInteraction 传进来</summary>
        public float Timeout { get; set; }

        /// <summary>连到多少的倍数放彩蛋，默认 100</summary>
        public int Step { get; set; }

        /// <summary>「连击 ×N」字号占屏幕宽度的比例</summary>
        public float ComboSizeRatio { get; set; }

        /// <summary>「连击 ×N」距屏幕顶部占屏幕高度的比例</summary>
        public float ComboTopRatio { get; set; }

        /// <summary>「N连抽」字高占屏幕宽度的比例</summary>
        public float MilestoneSizeRatio { get; set; }

        /// <summary>「N连抽」距屏幕顶部占屏幕高度的比例</summary>
        public float MilestoneTopRatio { get; set; }

        /// <summary>这一次 Confirm 是不是刚好踩到彩蛋</summary>
        public bool MilestoneHit { get; private set; }

        /// <summary>当前连击数（超时了就是 0）</summary>
        public int Count
        {
            get
            {
                if (_count <= 0 || Time.unscaledTime - _lastSlapAt >= Mathf.Max(0.1f, Timeout))
                {
                    return 0;
                }

                return _count;
            }
        }

        public MaidSlapComboHud()
        {
            Timeout = 2.5f;
            Step = 100;
            ComboSizeRatio = 0.085f;
            ComboTopRatio = 0.035f;
            MilestoneSizeRatio = 0.11f;
            MilestoneTopRatio = 0.12f;
        }

        public void Clear()
        {
            _count = 0;
            _lastSlapAt = -1000f;
            _milestone = 0;
            _lastAward = 0;
            _milestoneAt = -1000f;
            MilestoneHit = false;
        }

        /// <summary>扇中一巴掌，返回累计连击数，并顺便判断有没有踩到 100 的倍数</summary>
        public int Confirm()
        {
            float now = Time.unscaledTime;
            if (_count <= 0 || now - _lastSlapAt >= Mathf.Max(0.1f, Timeout) || now < _lastSlapAt)
            {
                _count = 0;
            }

            _count++;
            _lastSlapAt = now;
            MilestoneHit = false;

            // 模组 SlapMilestoneState：连击重新开始就清掉，只在 100 的倍数上放一次
            if (_count == 1)
            {
                _milestone = 0;
                _lastAward = 0;
            }

            int step = Mathf.Max(1, Step);
            if (_count % step == 0 && _count > _lastAward)
            {
                _milestone = _count;
                _lastAward = _count;
                _milestoneAt = now;
                MilestoneHit = true;
            }

            return _count;
        }

        public void Draw()
        {
            float now = Time.unscaledTime;
            DrawMilestone(now);

            int count = Count;
            if (count <= 0)
            {
                return;
            }

            float age = now - _lastSlapAt;
            float fade = Mathf.Min(1f, (Mathf.Max(0.1f, Timeout) - age) / FadeSeconds);
            int alpha = Mathf.Max(4, (int)(255f * fade));
            float pulse = age < PulseSeconds
                ? 0.35f * Mathf.Exp(-age / 0.085f) * Mathf.Cos(age / 0.07f)
                : 0f;
            float size = Screen.width * ComboSizeRatio * Mathf.Max(0.2f, 1f + pulse);
            float top = Mathf.Max(16f, Screen.height * ComboTopRatio) - pulse * 14f;
            DrawRainbowLabel("连击 ×" + count, Screen.width * 0.5f, top, Mathf.RoundToInt(size), alpha, now);
        }

        void DrawMilestone(float now)
        {
            if (_milestone <= 0)
            {
                return;
            }

            float age = now - _milestoneAt;
            if (age < 0f || age > TextSeconds)
            {
                return;
            }

            if (age < EffectSeconds)
            {
                DrawCelebration(age, now);
            }

            float pop;
            if (age < 0.18f)
            {
                pop = 1f + 0.6f * Mathf.Pow(1f - age / 0.18f, 3f);
            }
            else if (age < 0.48f)
            {
                pop = 1f + 0.05f * Mathf.Sin(Mathf.PI * (age - 0.18f) / 0.3f);
            }
            else
            {
                pop = 1f;
            }

            float fade = Mathf.Min(1f, (TextSeconds - age) / 0.55f);
            int alpha = Mathf.Max(4, (int)(fade * 255f));
            float height = Screen.width * MilestoneSizeRatio * pop;
            float top = Mathf.Max(42f, Screen.height * MilestoneTopRatio);
            DrawMilestoneText(_milestone + "连抽", Screen.width * 0.5f, top, height, alpha, now);
        }

        /// <summary>模组那套：两侧贴边的 8 条彩虹光带 + 每侧 64 个彩色粒子往里飞</summary>
        void DrawCelebration(float age, float now)
        {
            float progress = age / EffectSeconds;
            float unit = Screen.width / 1000f;
            for (int side = -1; side <= 1; side += 2)
            {
                for (int band = 0; band < 8; band++)
                {
                    int alpha = (int)(65f * (1f - progress) * (1f - band / 8f));
                    if (alpha <= 0)
                    {
                        continue;
                    }

                    Color color = Rainbow(((now % HueCycleSeconds) / HueCycleSeconds + band * 0.055f) % 1f,
                        0.7f, 1f, alpha);
                    float edge = band * 2f * unit;
                    float x = side < 0 ? edge : Screen.width - edge - 2f * unit;
                    FillRect(new Rect(x, 0f, 2f * unit, Screen.height), color);
                }

                for (int i = 0; i < ParticlesPerSide; i++)
                {
                    float delay = i % 4 * 0.055f;
                    if (age < delay)
                    {
                        continue;
                    }

                    float t = (age - delay) / Mathf.Max(0.001f, EffectSeconds - delay);
                    float travel = 1f - (1f - t) * (1f - t);
                    float seed = (i * 37 + (side + 1) * 11) % 97 / 97f;
                    float distance = (0.12f + seed * 0.20f) * Screen.width * travel;
                    float x = side < 0 ? 4f * unit + distance : Screen.width - 4f * unit - distance;
                    float y = Screen.height * (0.07f + (i * 19) % 86 / 100f)
                        - Screen.height * (0.04f + seed * 0.08f) * Mathf.Sin(t * Mathf.PI)
                        + t * t * Screen.height * 0.09f;
                    int alpha = Mathf.Max(0, (int)(245f * Mathf.Pow(1f - t, 0.85f)));
                    if (alpha <= 0)
                    {
                        continue;
                    }

                    float hue = ((now % HueCycleSeconds) / HueCycleSeconds + seed) % 1f;
                    Color color = Rainbow(hue, 0.62f, 1f, alpha);
                    DrawParticle(i % 4, x, y, unit, i * 31f + side * t * 240f, color, alpha);
                }
            }
        }

        /// <summary>模组里四种小图形：十字星 / 斜线 / 花瓣 / 箭头</summary>
        static void DrawParticle(int shape, float x, float y, float unit, float angle, Color color, int alpha)
        {
            Matrix4x4 saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, new Vector2(x, y));
            Color dim = new Color(color.r, color.g, color.b, color.a / 4f);
            switch (shape)
            {
                case 0:
                    FillRect(Rect(x, y, -2f, -7f, 2f, 7f, unit), dim);
                    FillRect(Rect(x, y, -7f, -2f, 7f, 2f, unit), dim);
                    FillRect(Rect(x, y, -1f, -5f, 1f, 5f, unit), color);
                    FillRect(Rect(x, y, -5f, -1f, 5f, 1f, unit), color);
                    FillRect(Rect(x, y, -1f, -1f, 1f, 1f, unit),
                        new Color(1f, 0.96f, 0.87f, color.a));
                    break;
                case 1:
                    FillRect(Rect(x, y, -8f, -1f, 0f, 1f, unit),
                        new Color(color.r, color.g, color.b, color.a / 3f));
                    FillRect(Rect(x, y, -2f, -1f, 3f, 2f, unit), color);
                    break;
                case 2:
                    FillRect(Rect(x, y, -2f, -4f, 2f, 4f, unit), color);
                    FillRect(Rect(x, y, -1f, -3f, 1f, -1f, unit),
                        new Color(1f, 0.96f, 0.87f, color.a));
                    break;
                default:
                    FillRect(Rect(x, y, -5f, -1f, 0f, 1f, unit), color);
                    FillRect(Rect(x, y, -1f, 0f, 1f, 4f, unit), color);
                    FillRect(Rect(x, y, 0f, 3f, 5f, 5f, unit), color);
                    break;
            }

            GUI.matrix = saved;
        }

        /// <summary>把模组那套"以粒子为中心、相对坐标、单位是 GUI 像素"的矩形换算成屏幕矩形</summary>
        static Rect Rect(float cx, float cy, float minX, float minY, float maxX, float maxY, float unit)
        {
            return new Rect(cx + minX * unit, cy + minY * unit, (maxX - minX) * unit, (maxY - minY) * unit);
        }

        /// <summary>模组那套「每个字一个色相、随时间滚动」的彩虹字</summary>
        void DrawRainbowLabel(string text, float centerX, float top, int fontSize, int alpha, float now)
        {
            Font font = GetFont();
            if (font == null || string.IsNullOrEmpty(text))
            {
                return;
            }

            EnsureStyle(font, fontSize, TextAnchor.UpperLeft);

            // 用 CalcSize 量宽度，不用 Font.GetCharacterInfo：中文走的是系统字体回退，
            // GetCharacterInfo 对回退字形返回 false，会一个字都画不出来
            float width = 0f;
            for (int i = 0; i < text.Length; i++)
            {
                width += Measure(text[i].ToString());
            }

            if (width < 1f)
            {
                // 实在量不出来就整句一起画，至少能看见
                GUI.color = Rainbow((now % HueCycleSeconds) / HueCycleSeconds, 0.68f, 1f, alpha);
                GUI.Label(new Rect(centerX - Screen.width * 0.4f, top, Screen.width * 0.8f, fontSize * 1.8f),
                    text, _style);
                GUI.color = Color.white;
                return;
            }

            float x = centerX - width * 0.5f;
            int index = 0;
            for (int i = 0; i < text.Length; i++)
            {
                string glyph = text[i].ToString();
                float advance = Measure(glyph);
                GUI.color = Rainbow(((now % HueCycleSeconds) / HueCycleSeconds + index * ComboHueStep) % 1f,
                    0.68f, 1f, alpha);
                GUI.Label(new Rect(x, top, advance + 2f, fontSize * 1.8f), glyph, _style);
                x += advance;
                index++;
            }

            GUI.color = Color.white;
        }

        float Measure(string glyph)
        {
            _measure.text = glyph;
            return _style.CalcSize(_measure).x;
        }

        /// <summary>「N连抽」用模组自带的位图字体；字体没导进来就退回普通彩虹字</summary>
        void DrawMilestoneText(string text, float centerX, float top, float glyphHeight, int alpha, float now)
        {
            Texture2D font = GetComboFont();
            if (font == null || !AllGlyphsAvailable(text))
            {
                DrawRainbowLabel(text, centerX, top, Mathf.RoundToInt(glyphHeight), alpha, now);
                return;
            }

            float width = glyphHeight * text.Length;
            float fit = Mathf.Min(1f, Screen.width * 0.78f / Mathf.Max(1f, width));
            glyphHeight *= fit;
            width = glyphHeight * text.Length;
            float x = centerX - width * 0.5f;

            // 描边：模组画了 5 次深紫底
            Color outlineDim = new Color(0x30 / 255f, 0x10 / 255f, 0x40 / 255f, alpha / 255f * 0.9f);
            Color outline = new Color(0x69 / 255f, 0x22 / 255f, 0x44 / 255f, alpha / 255f * 0.9f);
            int index = 0;
            for (int i = 0; i < text.Length; i++)
            {
                Rect uv = GlyphUv(text[i]);
                if (uv.width <= 0f)
                {
                    continue;
                }

                float gx = x + index * glyphHeight;
                float edge = Mathf.Max(1f, glyphHeight * 0.06f);
                DrawGlyph(font, uv, gx + edge, top + edge * 2f, glyphHeight, outline);
                DrawGlyph(font, uv, gx - edge, top, glyphHeight, outlineDim);
                DrawGlyph(font, uv, gx + edge, top, glyphHeight, outlineDim);
                DrawGlyph(font, uv, gx, top - edge, glyphHeight, outlineDim);
                DrawGlyph(font, uv, gx, top + edge, glyphHeight, outlineDim);
                index++;
            }

            index = 0;
            for (int i = 0; i < text.Length; i++)
            {
                Rect uv = GlyphUv(text[i]);
                if (uv.width <= 0f)
                {
                    continue;
                }

                float hue = ((now % HueCycleSeconds) / HueCycleSeconds + index * ComboHueStep) % 1f;
                DrawGlyph(font, uv, x + index * glyphHeight, top, glyphHeight,
                    Rainbow(hue, 0.68f, 1f, alpha));
                index++;
            }

            GUI.color = Color.white;
        }

        static void DrawGlyph(Texture2D font, Rect uv, float x, float y, float size, Color color)
        {
            GUI.color = color;
            GUI.DrawTextureWithTexCoords(new Rect(x, y, size, size), font, uv);
        }

        /// <summary>字符在位图字体的哪一格；找不到返回宽度 0</summary>
        static Rect GlyphUv(char c)
        {
            int index = -1;
            for (int row = 0; row < GlyphChars.Length && index < 0; row++)
            {
                int col = GlyphChars[row].IndexOf(c);
                if (col >= 0)
                {
                    index = row * GlyphColumns + col;
                }
            }

            if (index < 0)
            {
                return new Rect(0f, 0f, 0f, 0f);
            }

            int gridRow = index / GlyphColumns;
            int gridCol = index % GlyphColumns;
            float w = 1f / GlyphColumns;
            float h = 1f / GlyphChars.Length;
            // 图片第一行在最上面，而 uv 的原点在左下角
            return new Rect(gridCol * w, 1f - (gridRow + 1) * h, w, h);
        }

        /// <summary>有一个字不在位图字体里就整段退回普通字，别出现半个字叠在一起</summary>
        static bool AllGlyphsAvailable(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (GlyphUv(text[i]).width <= 0f)
                {
                    return false;
                }
            }

            return text.Length > 0;
        }

        static Color Rainbow(float hue, float saturation, float value, int alpha)
        {
            Color color = Color.HSVToRGB(Mathf.Repeat(hue, 1f), saturation, value);
            color.a = Mathf.Clamp01(alpha / 255f);
            return color;
        }

        static void FillRect(Rect rect, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
        }

        Font GetFont()
        {
            if (_font == null)
            {
                _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            return _font;
        }

        void EnsureStyle(Font font, int fontSize, TextAnchor anchor)
        {
            if (_style == null)
            {
                _style = new GUIStyle();
            }

            _style.font = font;
            _style.fontSize = fontSize;
            _style.alignment = anchor;
            _style.richText = false;
            _style.normal.textColor = Color.white;
            _style.clipping = TextClipping.Overflow;
        }

        Texture2D GetComboFont()
        {
            if (!_comboFontTried)
            {
                _comboFontTried = true;
                _comboFont = Resources.Load<Texture2D>("MoreAnimation/combo_display");
                if (_comboFont != null)
                {
                    _comboFont.filterMode = FilterMode.Point;
                    _comboFont.wrapMode = TextureWrapMode.Clamp;
                }
                else
                {
                    Debug.LogWarning("没找到连击位图字体 Assets/Resources/MoreAnimation/combo_display.png，"
                        + "「N连抽」改用普通字显示");
                }
            }

            return _comboFont;
        }
    }
}
