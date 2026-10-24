using System;
using System.Globalization;
using MaidHome.Core.Rendering;
using MaidHome.Core.Settings;
using UnityEditor;
using UnityEngine;

namespace MaidHome.EditorTools
{
    /// <summary>直接查看并设置当前世界的 MC 时间；Play 模式下保存后会通知常驻昼夜控制器。</summary>
    public sealed class DayNightEditorWindow : EditorWindow
    {
        const string MenuPath = "Tools/MaidHome/昼夜时间设置";
        const string TimeControlName = "MaidHomeDayNightTime";
        static readonly string[] ModeLabels = { "MC 时间", "现实时间" };

        GameSettings _settings;
        DayNightMode _mode;
        int _editTick;
        string _timeText = "06:00";
        string _status = "";
        MessageType _statusType = MessageType.Info;

        [MenuItem(MenuPath, false, 42)]
        static void Open()
        {
            DayNightEditorWindow window =
                GetWindow<DayNightEditorWindow>(false, "昼夜时间", true);
            window.minSize = new Vector2(430f, 300f);
            window.Show();
        }

        void OnEnable()
        {
            ReloadFromSettings();
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        void OnInspectorUpdate()
        {
            Repaint();
        }

        void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode
                || state == PlayModeStateChange.EnteredPlayMode)
            {
                ReloadFromSettings();
                Repaint();
            }
        }

        void OnGUI()
        {
            EnsureSettingsLoaded();

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("昼夜时间", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "这里修改的是当前设置里的世界时间。MC 时间一天 24000 tick；"
                + "现实时间模式会跟随本机时钟。",
                MessageType.None);

            int modeIndex = GUILayout.Toolbar((int)_mode, ModeLabels);
            if (modeIndex != (int)_mode)
            {
                ApplyMode((DayNightMode)modeIndex);
            }

            double current = ResolveTick(DateTime.UtcNow);
            EditorGUILayout.LabelField("当前时间", FormatClock(current));
            EditorGUILayout.LabelField("当前 tick", current.ToString("0", CultureInfo.InvariantCulture));
            EditorGUILayout.LabelField("月相", MoonPhaseLabel(CurrentMoonPhase()));

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("目标时间", EditorStyles.boldLabel);
            int tick = EditorGUILayout.IntSlider(_editTick, 0, 23999);
            if (tick != _editTick)
            {
                SetTargetTick(tick);
            }

            EditorGUILayout.BeginHorizontal();
            GUI.SetNextControlName(TimeControlName);
            _timeText = EditorGUILayout.TextField("HH:mm", _timeText);
            if (GUILayout.Button("解析", GUILayout.Width(70f)))
            {
                ParseClockText();
            }

            EditorGUILayout.EndHorizontal();
            HandleEnterKey();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("清晨 06:00"))
            {
                SetTargetTick(0);
            }

            if (GUILayout.Button("正午 12:00"))
            {
                SetTargetTick(6000);
            }

            if (GUILayout.Button("黄昏 18:00"))
            {
                SetTargetTick(12000);
            }

            if (GUILayout.Button("午夜 00:00"))
            {
                SetTargetTick(18000);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("同步到当前"))
            {
                SetTargetTick(current);
            }

            string applyLabel = _mode == DayNightMode.Minecraft
                ? "应用时间"
                : "切到 MC 时间并应用";
            if (GUILayout.Button(applyLabel, GUILayout.Height(28f)))
            {
                ApplyTargetTime();
            }

            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.HelpBox(_status, _statusType);
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("设置文件", EditorStyles.miniBoldLabel);
            EditorGUILayout.SelectableLabel(GameSettingsStore.FilePath,
                EditorStyles.miniLabel, GUILayout.Height(18f));
        }

        void EnsureSettingsLoaded()
        {
            GameSettings current = GameSettingsStore.Current;
            if (_settings == null || current != _settings || current.DayNight != _mode)
            {
                ReloadFromSettings();
            }
        }

        void ReloadFromSettings()
        {
            _settings = GameSettingsStore.Current;
            _mode = _settings.DayNight;
            SetTargetTick(ResolveTick(DateTime.UtcNow), false);
        }

        void ApplyMode(DayNightMode mode)
        {
            _settings = GameSettingsStore.Current;
            double current = ResolveTick(DateTime.UtcNow);
            long currentDay = ResolveDay(_mode);
            _settings.DayNight = mode;
            if (mode == DayNightMode.Minecraft)
            {
                _settings.DayNightAnchorDay = currentDay;
                _settings.DayNightAnchorUtcTicks = DateTime.UtcNow.Ticks;
                _settings.DayNightAnchorTick =
                    (float)MinecraftDayNight.Normalize(current);
            }

            string error;
            if (!GameSettingsStore.Save(out error))
            {
                SetStatus("保存失败：" + error, MessageType.Error);
                return;
            }

            _mode = mode;
            SetTargetTick(current);
            SetStatus("已切换到" + (mode == DayNightMode.Minecraft ? " MC " : "现实")
                + "时间模式。", MessageType.Info);
        }

        void ApplyTargetTime()
        {
            _settings = GameSettingsStore.Current;
            if (_settings.DayNight != DayNightMode.Minecraft)
            {
                // 先让运行中的昼夜控制器完成模式切换并重新锚定，第二次保存再写目标时间。
                long currentDay = ResolveDay(_mode);
                double current = ResolveTick(DateTime.UtcNow);
                _settings.DayNight = DayNightMode.Minecraft;
                _settings.DayNightAnchorDay = currentDay;
                _settings.DayNightAnchorUtcTicks = DateTime.UtcNow.Ticks;
                _settings.DayNightAnchorTick = (float)MinecraftDayNight.Normalize(current);
                string modeError;
                if (!GameSettingsStore.Save(out modeError))
                {
                    SetStatus("切换 MC 时间失败：" + modeError, MessageType.Error);
                    return;
                }
            }

            _settings = GameSettingsStore.Current;
            _settings.DayNight = DayNightMode.Minecraft;
            _settings.DayNightAnchorUtcTicks = DateTime.UtcNow.Ticks;
            _settings.DayNightAnchorTick = _editTick;

            string error;
            if (!GameSettingsStore.Save(out error))
            {
                SetStatus("保存失败：" + error, MessageType.Error);
                return;
            }

            _mode = DayNightMode.Minecraft;
            SetStatus("时间已设为 " + FormatClock(_editTick) + "（tick " + _editTick + "）。",
                MessageType.Info);
            Debug.Log("[昼夜] 编辑器工具把时间设为 " + FormatClock(_editTick)
                + "，tick = " + _editTick);
        }

        void ParseClockText()
        {
            int parsedTick;
            if (!TryParseClock(_timeText, out parsedTick))
            {
                SetStatus("时间格式应为 HH:mm，例如 06:30 或 18:00。", MessageType.Warning);
                return;
            }

            SetTargetTick(parsedTick);
            SetStatus("已选择 " + FormatClock(parsedTick) + "，点击「应用时间」生效。",
                MessageType.Info);
        }

        void HandleEnterKey()
        {
            Event current = Event.current;
            if (current.type != EventType.KeyDown
                || (current.keyCode != KeyCode.Return && current.keyCode != KeyCode.KeypadEnter)
                || GUI.GetNameOfFocusedControl() != TimeControlName)
            {
                return;
            }

            ParseClockText();
            current.Use();
        }

        void SetTargetTick(double tick, bool updateText = true)
        {
            _editTick = Mathf.RoundToInt((float)MinecraftDayNight.Normalize(tick)) % 24000;
            if (_editTick < 0)
            {
                _editTick += 24000;
            }

            if (updateText)
            {
                _timeText = FormatClock(_editTick);
            }
        }

        double ResolveTick(DateTime utcNow)
        {
            if (_settings == null)
            {
                return 0d;
            }

            if (_settings.DayNight == DayNightMode.RealTime)
            {
                return MinecraftDayNight.FromLocalTime(DateTime.Now);
            }

            if (_settings.DayNightAnchorUtcTicks <= 0)
            {
                return 0d;
            }

            return MinecraftDayNight.FromAnchor(_settings.DayNightAnchorUtcTicks,
                _settings.DayNightAnchorTick, utcNow);
        }

        int CurrentMoonPhase()
        {
            long day = _settings.DayNight == DayNightMode.RealTime
                ? LocalDay()
                : MinecraftDayNight.CompleteDaysFromAnchor(_settings.DayNightAnchorDay,
                    _settings.DayNightAnchorUtcTicks, _settings.DayNightAnchorTick, DateTime.UtcNow);
            return MinecraftDayNight.MoonPhase(day);
        }

        long ResolveDay(DayNightMode mode)
        {
            return mode == DayNightMode.RealTime
                ? LocalDay()
                : MinecraftDayNight.CompleteDaysFromAnchor(_settings.DayNightAnchorDay,
                    _settings.DayNightAnchorUtcTicks, _settings.DayNightAnchorTick, DateTime.UtcNow);
        }

        static long LocalDay()
        {
            return DateTime.Now.Date.Ticks / TimeSpan.TicksPerDay;
        }

        static string MoonPhaseLabel(int phase)
        {
            switch (phase)
            {
                case 0:
                    return "满月";
                case 1:
                    return "亏凸月";
                case 2:
                    return "下弦月";
                case 3:
                    return "残月";
                case 4:
                    return "新月";
                case 5:
                    return "娥眉月";
                case 6:
                    return "上弦月";
                default:
                    return "盈凸月";
            }
        }

        static bool TryParseClock(string text, out int tick)
        {
            tick = 0;
            string[] parts = (text ?? "").Trim().Split(':');
            if (parts.Length != 2)
            {
                return false;
            }

            int hour;
            int minute;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out hour)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out minute)
                || hour < 0 || hour > 23 || minute < 0 || minute > 59)
            {
                return false;
            }

            int seconds = hour * 3600 + minute * 60;
            tick = Mathf.RoundToInt((float)MinecraftDayNight.Normalize(
                (seconds - 6 * 3600) * MinecraftDayNight.TicksPerDay / 86400d));
            return true;
        }

        static string FormatClock(double tick)
        {
            double seconds = (MinecraftDayNight.Normalize(tick) / MinecraftDayNight.TicksPerDay
                * 86400d + 6d * 3600d) % 86400d;
            int totalMinutes = (int)Math.Floor(seconds / 60d + 0.0001d) % 1440;
            return (totalMinutes / 60).ToString("00", CultureInfo.InvariantCulture)
                + ":" + (totalMinutes % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        void SetStatus(string message, MessageType type)
        {
            _status = message;
            _statusType = type;
            Repaint();
        }
    }
}
