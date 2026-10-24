using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using MaidHome.Core.Json;
using MaidHome.Core.Settings;
using UnityEngine;
using UnityEngine.Networking;

namespace MaidHome.Interop.Ai
{
    [DisallowMultipleComponent]
    public sealed class AiChatClient : MonoBehaviour
    {
        const int TimeoutSeconds = 90;
        const int MaxContextMessages = 32;

        UnityWebRequest _request;
        bool _busy;

        public bool Busy
        {
            get { return _busy; }
        }

        public void Send(AiChatRequest request, Action<AiChatReply> onSuccess, Action<string> onFailure)
        {
            if (!GameSettingsStore.Current.AiEnabled)
            {
                if (onFailure != null) onFailure("AI 功能已关闭");
                return;
            }

            if (_busy)
            {
                if (onFailure != null) onFailure("上一条消息还在生成");
                return;
            }

            StartCoroutine(SendRoutine(request, onSuccess, onFailure));
        }

        public void Cancel()
        {
            if (_request != null)
            {
                _request.Abort();
            }

            _busy = false;
        }

        IEnumerator SendRoutine(AiChatRequest request, Action<AiChatReply> onSuccess, Action<string> onFailure)
        {
            _busy = true;
            GameSettings settings = GameSettingsStore.Current;
            if (!settings.AiEnabled)
            {
                _busy = false;
                if (onFailure != null) onFailure("AI 功能已关闭");
                yield break;
            }

            AiChatSiteInfo site = AiChatSiteCatalog.Get(settings.AiSite);
            if (string.IsNullOrEmpty(settings.AiApiKey))
            {
                _busy = false;
                if (onFailure != null) onFailure("还没有设置 AI Key，请到设置里的「AI 聊天」填写");
                yield break;
            }

            byte[] body;
            try
            {
                body = Encoding.UTF8.GetBytes(BuildRequestJson(site, request));
                _request = new UnityWebRequest(site.Url, UnityWebRequest.kHttpVerbPOST);
            }
            catch (Exception exception)
            {
                _busy = false;
                if (onFailure != null) onFailure("AI 请求创建失败：" + exception.Message);
                yield break;
            }

            _request.uploadHandler = new UploadHandlerRaw(body);
            _request.downloadHandler = new DownloadHandlerBuffer();
            _request.SetRequestHeader("Content-Type", "application/json");
            _request.SetRequestHeader("Authorization", "Bearer " + settings.AiApiKey.Trim());
            _request.timeout = TimeoutSeconds;
            yield return _request.SendWebRequest();

            UnityWebRequest requestHandle = _request;
            _request = null;
            _busy = false;
            try
            {
                string responseText = requestHandle.downloadHandler != null
                    ? requestHandle.downloadHandler.text
                    : "";
                if (requestHandle.result != UnityWebRequest.Result.Success)
                {
                    string detail = ExtractError(responseText);
                    if (onFailure != null) onFailure(detail);
                    yield break;
                }

                AiChatReply reply;
                string error;
                if (!TryParseReply(responseText, out reply, out error))
                {
                    if (onFailure != null) onFailure(error);
                    yield break;
                }

                if (onSuccess != null) onSuccess(reply);
            }
            finally
            {
                requestHandle.Dispose();
            }
        }

        static string BuildRequestJson(AiChatSiteInfo site, AiChatRequest request)
        {
            List<JsonValue> messages = new List<JsonValue>();
            AddMessage(messages, "system", request.SystemPrompt);
            AddMessage(messages, "system", MaidAiPromptBuilder.PhoneBoundary);
            if (!string.IsNullOrEmpty(request.HistorySummary))
            {
                AddMessage(messages, "system", "此前聊天的摘要：\n" + request.HistorySummary);
            }

            int start = Mathf.Max(0, request.History.Count - MaxContextMessages);
            for (int i = start; i < request.History.Count; i++)
            {
                AiChatMessage message = request.History[i];
                AddConversationMessage(messages, message);
            }

            if (!string.IsNullOrWhiteSpace(request.UserInput))
            {
                AddMessage(messages, "user", request.UserInput);
            }

            for (int i = 0; i < request.ExtraMessages.Count; i++)
            {
                AddConversationMessage(messages, request.ExtraMessages[i]);
            }

            Dictionary<string, JsonValue> root = new Dictionary<string, JsonValue>();
            root["model"] = JsonValue.FromString(site.Model);
            root["messages"] = JsonValue.FromArray(messages);
            root["temperature"] = JsonValue.FromNumber(0.8);
            root["max_tokens"] = JsonValue.FromNumber(request.MaxTokens);
            root["stream"] = JsonValue.FromBool(false);
            if (!string.IsNullOrEmpty(request.ToolsJson))
            {
                try
                {
                    root["tools"] = MiniJson.Parse(request.ToolsJson);
                    if (!string.IsNullOrEmpty(request.ToolChoice))
                    {
                        root["tool_choice"] = JsonValue.FromString(request.ToolChoice);
                    }
                }
                catch (Exception)
                {
                    root.Remove("tools");
                }
            }

            return MiniJsonWriter.Write(JsonValue.FromObject(root));
        }

        static void AddMessage(List<JsonValue> messages, string role, string text)
        {
            Dictionary<string, JsonValue> item = new Dictionary<string, JsonValue>();
            item["role"] = JsonValue.FromString(role);
            item["content"] = JsonValue.FromString(text ?? "");
            messages.Add(JsonValue.FromObject(item));
        }

        static void AddConversationMessage(List<JsonValue> messages, AiChatMessage message)
        {
            if (message == null || message.Status != "complete") return;
            string text = string.IsNullOrEmpty(message.RawText) ? message.Text : message.RawText;
            if (message.Role == "user")
            {
                if (!string.IsNullOrWhiteSpace(text)) AddMessage(messages, "user", text);
                return;
            }

            if (message.Role == "tool")
            {
                Dictionary<string, JsonValue> tool = new Dictionary<string, JsonValue>();
                tool["role"] = JsonValue.FromString("tool");
                tool["content"] = JsonValue.FromString(text ?? "");
                tool["tool_call_id"] = JsonValue.FromString(message.ToolCallId ?? "");
                messages.Add(JsonValue.FromObject(tool));
                return;
            }

            if (message.Role != "assistant") return;
            bool hasTools = !string.IsNullOrEmpty(message.ToolCallsJson);
            if (string.IsNullOrWhiteSpace(text) && !hasTools) return;
            Dictionary<string, JsonValue> assistant = new Dictionary<string, JsonValue>();
            assistant["role"] = JsonValue.FromString("assistant");
            assistant["content"] = string.IsNullOrWhiteSpace(text)
                ? JsonValue.Null
                : JsonValue.FromString(text);
            if (hasTools)
            {
                try { assistant["tool_calls"] = MiniJson.Parse(message.ToolCallsJson); }
                catch (Exception) { return; }
            }

            messages.Add(JsonValue.FromObject(assistant));
        }

        static bool TryParseReply(string json, out AiChatReply reply, out string error)
        {
            reply = new AiChatReply();
            error = "";
            if (string.IsNullOrEmpty(json))
            {
                error = "AI 返回了空响应";
                return false;
            }

            try
            {
                JsonValue root = MiniJson.Parse(json);
                if (root.Has("error"))
                {
                    error = root["error"]["message"].AsString("AI 请求失败");
                    return false;
                }

                JsonValue choice = root["choices"][0];
                string content = choice["message"]["content"].AsString("").Trim();
                int separator = content.IndexOf("---", StringComparison.Ordinal);
                if (separator >= 0)
                {
                    reply.Text = content.Substring(0, separator).Trim();
                    reply.TtsText = content.Substring(separator + 3).Trim();
                }
                else
                {
                    reply.Text = content;
                }
                reply.FinishReason = choice["finish_reason"].AsString("");
                if (choice["message"].Has("tool_calls"))
                {
                    reply.ToolCallsJson = MiniJsonWriter.Write(choice["message"]["tool_calls"]);
                }

                if (string.IsNullOrEmpty(reply.Text) && string.IsNullOrEmpty(reply.ToolCallsJson))
                {
                    error = "AI 没有返回文字内容";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error = "AI 响应解析失败：" + exception.Message;
                return false;
            }
        }

        static string ExtractError(string json)
        {
            try
            {
                JsonValue root = MiniJson.Parse(json);
                string message = root["error"]["message"].AsString("");
                if (!string.IsNullOrEmpty(message)) return message;
            }
            catch (Exception)
            {
                // 非 JSON 错误页直接退回 HTTP 摘要
            }

            return string.IsNullOrEmpty(json) ? "AI 请求失败" : json.Substring(0, Mathf.Min(json.Length, 300));
        }
    }
}
