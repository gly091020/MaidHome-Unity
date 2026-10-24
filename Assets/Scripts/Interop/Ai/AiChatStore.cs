using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using MaidHome.Core.Json;
using MaidHome.Core.Storage;
using MaidHome.Interop.Maid;
using UnityEngine;

namespace MaidHome.Interop.Ai
{
    public static class AiChatStore
    {
        public const string FileName = "ai_chat.json.gz";
        public const int MaxCompressedBytes = 2 * 1024 * 1024;
        public const int MaxJsonBytes = 2 * 1024 * 1024;

        public static string PathOf(MaidSaveData maid)
        {
            return maid == null || string.IsNullOrEmpty(maid.Folder) ? "" : Path.Combine(maid.Folder, FileName);
        }

        public static AiChatRecord Load(MaidSaveData maid, out string error)
        {
            error = "";
            AiChatRecord empty = CreateEmpty(maid);
            string path = PathOf(maid);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return empty;
            }

            try
            {
                byte[] compressed = File.ReadAllBytes(path);
                if (compressed.Length == 0 || compressed.Length > MaxCompressedBytes)
                {
                    error = "AI 聊天压缩包大小无效";
                    return empty;
                }

                byte[] json = Decompress(compressed);
                AiChatRecord record = Parse(Encoding.UTF8.GetString(json));
                if (record.SchemaVersion > AiChatRecord.CurrentVersion)
                {
                    error = "AI 聊天数据版本高于当前 App";
                    return empty;
                }

                return record;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return empty;
            }
        }

        public static bool Save(MaidSaveData maid, AiChatRecord record, out string error)
        {
            error = "";
            string path = PathOf(maid);
            if (string.IsNullOrEmpty(path) || record == null)
            {
                error = "没有可保存的 AI 聊天目录";
                return false;
            }

            try
            {
                byte[] json = Encoding.UTF8.GetBytes(ToJson(record));
                if (json.Length > MaxJsonBytes)
                {
                    error = "AI 聊天解压后超过 2 MiB";
                    return false;
                }

                byte[] compressed = Compress(json);
                AppPaths.EnsureDirectory(maid.Folder);
                string temp = path + ".tmp";
                File.WriteAllBytes(temp, compressed);
                if (File.Exists(path))
                {
                    File.Copy(path, path + ".bak", true);
                    File.Delete(path);
                }

                File.Move(temp, path);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public static bool ValidateCompressedFile(string path, out string error)
        {
            error = "";
            try
            {
                if (!File.Exists(path))
                {
                    return true;
                }

                byte[] compressed = File.ReadAllBytes(path);
                byte[] json = Decompress(compressed);
                AiChatRecord record = Parse(Encoding.UTF8.GetString(json));
                if (record.SchemaVersion != AiChatRecord.CurrentVersion)
                {
                    error = "AI 聊天版本无效: " + record.SchemaVersion;
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public static AiChatRecord Parse(string json)
        {
            JsonValue root = MiniJson.Parse(json);
            AiChatRecord record = new AiChatRecord();
            record.SchemaVersion = root["schema_version"].AsInt(AiChatRecord.CurrentVersion);
            record.MaidUuid = root["maid_uuid"].AsString("");
            record.HistorySummary = root["history_summary"].AsString("");
            record.LastTokenUsage = root["last_token_usage"].AsInt(0);
            record.BaseMessageCount = root["base_message_count"].AsInt(0);
            record.SlapHitCount = root["slap_hit_count"].AsInt(0);
            record.CakeHitCount = root["cake_hit_count"].AsInt(0);
            record.LlmSite = root["llm_ref"]["site"].AsString("");
            record.LlmModel = root["llm_ref"]["model"].AsString("");

            JsonValue profile = root["profile"];
            record.Profile.MaidName = profile["maid_name"].AsString("");
            record.Profile.OwnerName = profile["owner_name"].AsString("");
            record.Profile.ModelId = profile["model_id"].AsString("");
            record.Profile.ChatLanguage = profile["chat_language"].AsString("zh_cn");
            record.Profile.CustomSetting = profile["custom_setting"].AsString("");
            record.Profile.ResolvedSystemPrompt = profile["resolved_system_prompt"].AsString("");
            record.Profile.GeneratedPersona = profile["generated_persona"].AsString("");
            record.Profile.PersonaSource = profile["persona_source"].AsString("missing");

            JsonValue messages = root["messages"];
            for (int i = 0; i < messages.Count && record.Messages.Count < AiChatRecord.MaxMessages; i++)
            {
                JsonValue node = messages[i];
                AiChatMessage message = new AiChatMessage();
                message.Id = node["id"].AsString("");
                message.RequestId = node["request_id"].AsString("");
                message.Role = node["role"].AsString("");
                message.Text = node["text"].AsString("");
                message.RawText = node["raw_text"].AsString(message.Text);
                message.TtsText = node["tts_text"].AsString("");
                message.GameTime = node["game_time"].AsLong(0L);
                message.Origin = node["origin"].AsString("");
                message.Status = node["status"].AsString("complete");
                message.ToolCallId = node["tool_call_id"].AsString("");
                if (node.Has("tool_calls"))
                {
                    message.ToolCallsJson = MiniJsonWriter.Write(node["tool_calls"]);
                }

                record.Messages.Add(message);
            }

            if (record.BaseMessageCount < 0 || record.BaseMessageCount > record.Messages.Count)
            {
                record.BaseMessageCount = 0;
            }

            return record;
        }

        public static string ToJson(AiChatRecord record)
        {
            Dictionary<string, JsonValue> root = new Dictionary<string, JsonValue>();
            root["schema_version"] = Number(record.SchemaVersion);
            root["maid_uuid"] = Text(record.MaidUuid);
            root["history_summary"] = Text(record.HistorySummary);
            root["last_token_usage"] = Number(record.LastTokenUsage);
            root["base_message_count"] = Number(record.BaseMessageCount);
            root["slap_hit_count"] = Number(record.SlapHitCount);
            root["cake_hit_count"] = Number(record.CakeHitCount);

            Dictionary<string, JsonValue> llm = new Dictionary<string, JsonValue>();
            llm["site"] = Text(record.LlmSite);
            llm["model"] = Text(record.LlmModel);
            root["llm_ref"] = JsonValue.FromObject(llm);

            Dictionary<string, JsonValue> profile = new Dictionary<string, JsonValue>();
            profile["maid_name"] = Text(record.Profile.MaidName);
            profile["owner_name"] = Text(record.Profile.OwnerName);
            profile["model_id"] = Text(record.Profile.ModelId);
            profile["chat_language"] = Text(record.Profile.ChatLanguage);
            profile["custom_setting"] = Text(record.Profile.CustomSetting);
            profile["resolved_system_prompt"] = Text(record.Profile.ResolvedSystemPrompt);
            profile["generated_persona"] = Text(record.Profile.GeneratedPersona);
            profile["persona_source"] = Text(record.Profile.PersonaSource);
            root["profile"] = JsonValue.FromObject(profile);

            List<JsonValue> messages = new List<JsonValue>();
            for (int i = 0; i < record.Messages.Count; i++)
            {
                AiChatMessage message = record.Messages[i];
                Dictionary<string, JsonValue> node = new Dictionary<string, JsonValue>();
                node["id"] = Text(message.Id);
                node["request_id"] = Text(message.RequestId);
                node["role"] = Text(message.Role);
                node["text"] = Text(message.Text);
                node["raw_text"] = Text(message.RawText);
                node["tts_text"] = Text(message.TtsText);
                node["game_time"] = Number(message.GameTime);
                node["origin"] = Text(message.Origin);
                node["status"] = Text(message.Status);
                node["tool_call_id"] = Text(message.ToolCallId);
                if (!string.IsNullOrEmpty(message.ToolCallsJson))
                {
                    try { node["tool_calls"] = MiniJson.Parse(message.ToolCallsJson); }
                    catch (Exception) { }
                }

                messages.Add(JsonValue.FromObject(node));
            }

            root["messages"] = JsonValue.FromArray(messages);
            return MiniJsonWriter.Write(JsonValue.FromObject(root), true) + "\n";
        }

        static AiChatRecord CreateEmpty(MaidSaveData maid)
        {
            AiChatRecord record = new AiChatRecord();
            record.MaidUuid = maid != null ? maid.Id : "";
            if (maid != null)
            {
                record.Profile.MaidName = maid.Name;
                record.Profile.OwnerName = maid.OwnerName;
                record.Profile.ModelId = Path.GetFileNameWithoutExtension(maid.ModelFile);
            }

            return record;
        }

        static JsonValue Text(string value)
        {
            return JsonValue.FromString(value ?? "");
        }

        static JsonValue Number(long value)
        {
            return JsonValue.FromNumber(value);
        }

        static byte[] Compress(byte[] data)
        {
            using (MemoryStream output = new MemoryStream())
            {
                using (GZipStream gzip = new GZipStream(output, CompressionMode.Compress, true))
                {
                    gzip.Write(data, 0, data.Length);
                }

                return output.ToArray();
            }
        }

        static byte[] Decompress(byte[] data)
        {
            if (data.Length == 0 || data.Length > MaxCompressedBytes)
            {
                throw new InvalidDataException("AI 聊天压缩包大小无效");
            }

            using (MemoryStream input = new MemoryStream(data))
            using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress))
            using (MemoryStream output = new MemoryStream())
            {
                byte[] buffer = new byte[8192];
                int total = 0;
                int read;
                while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += read;
                    if (total > MaxJsonBytes)
                    {
                        throw new InvalidDataException("AI 聊天解压后超过 2 MiB");
                    }

                    output.Write(buffer, 0, read);
                }

                return output.ToArray();
            }
        }
    }
}
