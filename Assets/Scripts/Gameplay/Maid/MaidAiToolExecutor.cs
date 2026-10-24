using System;
using System.Collections.Generic;
using MaidHome.Core.Json;
using MaidHome.Core.Settings;
using MaidHome.Interop.Ai;
using MaidHome.Interop.Bedrock;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    public static class MaidAiToolExecutor
    {
        sealed class ParsedCall
        {
            public string Id;
            public string Name;
            public string Arguments;
        }

        public static bool ExecuteAll(MaidAgent agent, string toolCallsJson, string assistantText,
            out List<AiChatMessage> continuation, out string summary)
        {
            continuation = new List<AiChatMessage>();
            summary = "";
            List<ParsedCall> calls = Parse(toolCallsJson, out string parseError);
            if (calls.Count == 0)
            {
                summary = "工具调用无效：" + parseError;
                return false;
            }

            AiChatMessage assistant = new AiChatMessage();
            assistant.Id = Guid.NewGuid().ToString("N");
            assistant.Role = "assistant";
            assistant.Text = assistantText ?? "";
            assistant.RawText = assistant.Text;
            assistant.Status = "complete";
            assistant.Origin = "phone";
            assistant.ToolCallsJson = toolCallsJson;
            continuation.Add(assistant);

            int succeeded = 0;
            for (int i = 0; i < calls.Count; i++)
            {
                ParsedCall call = calls[i];
                string result = Execute(agent, call);
                if (result.StartsWith("成功", StringComparison.Ordinal)) succeeded++;
                AiChatMessage tool = new AiChatMessage();
                tool.Id = Guid.NewGuid().ToString("N");
                tool.Role = "tool";
                tool.Text = result;
                tool.RawText = result;
                tool.Status = "complete";
                tool.Origin = "phone";
                tool.ToolCallId = call.Id ?? "";
                continuation.Add(tool);
            }

            summary = "工具调用完成：" + succeeded + "/" + calls.Count + " 个成功";
            return true;
        }

        static string Execute(MaidAgent agent, ParsedCall call)
        {
            if (!GameSettingsStore.Current.AiEnabled) return "失败：AI 功能已关闭";
            if (!string.Equals(call.Name, MaidAiExpressionTool.ToolName, StringComparison.Ordinal))
            {
                return "失败：不允许调用工具 " + call.Name;
            }

            JsonValue arguments;
            try
            {
                arguments = string.IsNullOrWhiteSpace(call.Arguments)
                    ? JsonValue.Null
                    : MiniJson.Parse(call.Arguments);
            }
            catch (Exception exception)
            {
                return "失败：参数不是合法 JSON：" + exception.Message;
            }

            string chineseName = arguments["animation"].AsString("");
            if (string.IsNullOrWhiteSpace(chineseName))
            {
                return "失败：缺少必填参数 animation；没有播放任何默认动画";
            }

            string clipName;
            if (!MaidAiExpressionTool.TryResolve(chineseName, out clipName))
            {
                return "失败：动画名不在当前允许的 AI 可用动画中：" + chineseName;
            }

            BedrockAnimationPlayer animation = agent != null ? agent.GetComponent<BedrockAnimationPlayer>() : null;
            if (animation == null || !animation.HasClip(clipName))
            {
                return "失败：当前女仆没有动画 " + chineseName;
            }

            MaidAiExpressionPlayer player = agent.GetComponent<MaidAiExpressionPlayer>();
            if (player == null) player = agent.gameObject.AddComponent<MaidAiExpressionPlayer>();
            string error;
            if (!player.PlayClip(clipName, out error))
            {
                return "失败：" + error;
            }

            return "成功：已播放动画「" + chineseName + "」";
        }

        static List<ParsedCall> Parse(string json, out string error)
        {
            error = "";
            List<ParsedCall> result = new List<ParsedCall>();
            try
            {
                JsonValue root = MiniJson.Parse(json ?? "");
                for (int i = 0; i < root.Count; i++)
                {
                    JsonValue item = root[i];
                    JsonValue function = item["function"];
                    ParsedCall call = new ParsedCall();
                    call.Id = item["id"].AsString("");
                    call.Name = function["name"].AsString("");
                    JsonValue arguments = function["arguments"];
                    call.Arguments = arguments.IsString
                        ? arguments.AsString("")
                        : arguments.IsNull ? "" : MiniJsonWriter.Write(arguments);
                    if (!string.IsNullOrEmpty(call.Id) && !string.IsNullOrEmpty(call.Name))
                    {
                        result.Add(call);
                    }
                }
            }
            catch (Exception exception)
            {
                error = exception.Message;
            }

            if (result.Count == 0 && string.IsNullOrEmpty(error))
            {
                error = "没有可执行的 tool call";
            }

            return result;
        }
    }
}
