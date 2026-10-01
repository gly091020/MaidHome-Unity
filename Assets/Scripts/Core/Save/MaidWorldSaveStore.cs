using System;
using System.Globalization;
using System.IO;
using System.Text;
using MaidHome.Core.Json;
using MaidHome.Core.Storage;
using UnityEngine;

namespace MaidHome.Core.Save
{
    /// <summary>
    /// slot0.json 的原子读写。先写 .tmp，成功后再替换正式文件，并保留 .bak；
    /// 正式文件坏了就退回备份，保证不会因为一次写盘失败把存档弄没。
    /// </summary>
    public static class MaidWorldSaveStore
    {
        public static string SavePath
        {
            get { return Path.Combine(AppPaths.SavesRoot, "slot0.json"); }
        }

        public static string BackupPath
        {
            get { return SavePath + ".bak"; }
        }

        public static string TempPath
        {
            get { return SavePath + ".tmp"; }
        }

        public static MaidWorldSave Load()
        {
            MaidWorldSave save = TryLoad(SavePath);
            if (save != null)
            {
                return save;
            }

            save = TryLoad(BackupPath);
            if (save != null)
            {
                Debug.LogWarning("slot0.json 读取失败，已改用备份: " + BackupPath);
                return save;
            }

            return new MaidWorldSave();
        }

        public static void Save(MaidWorldSave save)
        {
            if (save == null)
            {
                return;
            }

            AppPaths.EnsureDirectory(AppPaths.SavesRoot);
            string json = ToJson(save);

            File.WriteAllText(TempPath, json, new UTF8Encoding(false));
            if (File.Exists(SavePath))
            {
                try
                {
                    File.Replace(TempPath, SavePath, BackupPath);
                    return;
                }
                catch (Exception error)
                {
                    // Android/Mono 上 File.Replace 不一定可用，退回复制+移动
                    Debug.LogWarning("File.Replace 失败，改用普通替换: " + error.Message);
                    File.Copy(SavePath, BackupPath, true);
                    File.Delete(SavePath);
                }
            }

            File.Move(TempPath, SavePath);
        }

        static MaidWorldSave TryLoad(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return Parse(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception error)
            {
                Debug.LogWarning("存档读取失败 " + path + ": " + error.Message);
                return null;
            }
        }

        public static MaidWorldSave Parse(string json)
        {
            MaidWorldSave save = new MaidWorldSave();
            JsonValue root = MiniJson.Parse(json);
            save.Version = root["version"].AsInt(MaidWorldSave.CurrentVersion);
            save.HouseId = root["house_id"].AsString("");

            JsonValue maids = root["maids"];
            if (!maids.IsArray)
            {
                return save;
            }

            for (int i = 0; i < maids.Count; i++)
            {
                JsonValue node = maids[i];
                MaidWorldRecord record = new MaidWorldRecord();
                record.Id = node["id"].AsString("");
                record.HouseId = node["house_id"].AsString("");
                record.InBag = node["in_bag"].AsBool(true);
                record.Position = new Vector3(
                    node["x"].AsFloat(0f),
                    node["y"].AsFloat(0f),
                    node["z"].AsFloat(0f));
                record.RotationY = node["rotation_y"].AsFloat(0f);

                if (!string.IsNullOrEmpty(record.Id))
                {
                    save.Maids.Add(record);
                }
            }

            return save;
        }

        public static string ToJson(MaidWorldSave save)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("{\n");
            builder.Append("  \"version\": ").Append(save.Version).Append(",\n");
            builder.Append("  \"house_id\": ");
            AppendQuoted(builder, save.HouseId);
            builder.Append(",\n");
            builder.Append("  \"maids\": [");

            for (int i = 0; i < save.Maids.Count; i++)
            {
                MaidWorldRecord record = save.Maids[i];
                builder.Append(i == 0 ? "\n" : ",\n");
                builder.Append("    { \"id\": ");
                AppendQuoted(builder, record.Id);
                builder.Append(", \"house_id\": ");
                AppendQuoted(builder, record.HouseId);
                builder.Append(", \"in_bag\": ").Append(record.InBag ? "true" : "false");
                builder.Append(", \"x\": ").Append(Number(record.Position.x));
                builder.Append(", \"y\": ").Append(Number(record.Position.y));
                builder.Append(", \"z\": ").Append(Number(record.Position.z));
                builder.Append(", \"rotation_y\": ").Append(Number(record.RotationY));
                builder.Append(" }");
            }

            builder.Append(save.Maids.Count > 0 ? "\n  ]\n" : "]\n");
            builder.Append("}\n");
            return builder.ToString();
        }

        static string Number(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        static void AppendQuoted(StringBuilder builder, string value)
        {
            builder.Append('"');
            if (value != null)
            {
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    switch (c)
                    {
                        case '"': builder.Append("\\\""); break;
                        case '\\': builder.Append("\\\\"); break;
                        case '\n': builder.Append("\\n"); break;
                        case '\r': builder.Append("\\r"); break;
                        case '\t': builder.Append("\\t"); break;
                        default: builder.Append(c); break;
                    }
                }
            }

            builder.Append('"');
        }
    }
}
