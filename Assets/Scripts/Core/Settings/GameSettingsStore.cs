using System;
using System.IO;
using System.Reflection;
using System.Text;
using MaidHome.Core.Storage;
using UnityEngine;

namespace MaidHome.Core.Settings
{
    /// <summary>config/settings.json 的读写；坏档退回默认值，写盘先写 .tmp 再替换。</summary>
    public static class GameSettingsStore
    {
        public const int CurrentVersion = 5;

        static GameSettings _current;

        public static event Action Changed;

        public static string ConfigDirectory
        {
            get { return Path.Combine(AppPaths.SavesRoot, "config"); }
        }

        public static string FilePath
        {
            get { return Path.Combine(ConfigDirectory, "settings.json"); }
        }

        public static GameSettings Current
        {
            get
            {
                if (_current == null)
                {
                    Load();
                }

                return _current;
            }
        }

        public static void Load()
        {
            _current = new GameSettings();
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath, Encoding.UTF8);
                    GameSettings loaded = JsonUtility.FromJson<GameSettings>(json);
                    if (loaded != null)
                    {
                        _current = loaded;
                    }
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning("[Settings] 读取失败，使用默认设置：" + error.Message + "（" + FilePath + "）");
            }

            if (_current.Version < 5)
            {
                // 旧设置没有 AI 总开关，升级时保持原有 AI 功能可用。
                _current.AiEnabled = true;
            }

            if (_current.Version < CurrentVersion)
            {
                _current.Version = CurrentVersion;
            }

            ClampFields(_current);
        }

        public static bool Save(out string error)
        {
            error = "";
            try
            {
                Directory.CreateDirectory(ConfigDirectory);
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(Current, true), new UTF8Encoding(false));
                if (File.Exists(FilePath))
                {
                    File.Copy(FilePath, FilePath + ".bak", true);
                    File.Delete(FilePath);
                }

                File.Move(temp, FilePath);
                if (Changed != null)
                {
                    Changed();
                }

                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                Debug.LogWarning("[Settings] 保存失败：" + error + "（" + FilePath + "）");
                return false;
            }
        }

        public static bool Save()
        {
            string error;
            return Save(out error);
        }

        /// <summary>只发变更通知（滑动条拖动时先让音量实时生效，保存由界面防抖）。</summary>
        public static void NotifyChanged()
        {
            if (Changed != null)
            {
                Changed();
            }
        }

        static void ClampFields(GameSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            FieldInfo[] fields = typeof(GameSettings).GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                SettingAttribute attribute = fields[i].GetCustomAttribute<SettingAttribute>();
                if (attribute == null || !attribute.HasRange)
                {
                    continue;
                }

                object value = fields[i].GetValue(settings);
                if (fields[i].FieldType == typeof(int))
                {
                    int current = value is int ? (int)value : 0;
                    fields[i].SetValue(settings, Mathf.Clamp(current,
                        Mathf.RoundToInt(attribute.Min), Mathf.RoundToInt(attribute.Max)));
                }
                else if (fields[i].FieldType == typeof(float))
                {
                    float current = value is float ? (float)value : 0f;
                    fields[i].SetValue(settings, Mathf.Clamp(current, attribute.Min, attribute.Max));
                }
            }

            for (int i = 0; i < fields.Length; i++)
            {
                object value = fields[i].GetValue(settings);
                if (fields[i].FieldType == typeof(string) && value == null)
                {
                    fields[i].SetValue(settings, "");
                }
                else if (fields[i].FieldType.IsEnum && value != null
                    && !Enum.IsDefined(fields[i].FieldType, value))
                {
                    Array values = Enum.GetValues(fields[i].FieldType);
                    if (values.Length > 0)
                    {
                        fields[i].SetValue(settings, values.GetValue(0));
                    }
                }
            }
        }
    }
}
