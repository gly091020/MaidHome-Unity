using System;
using System.Collections.Generic;
using System.Globalization;
using MaidHome.Core.Settings;
using MaidHome.Interop.Ai;
using MaidHome.Interop.Maid;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    [DisallowMultipleComponent]
    public sealed class MaidAiChatController : MonoBehaviour
    {
        sealed class FeelingEvent
        {
            public string Id;
            public string Name;
            public string EventText;
        }

        public event Action Changed;
        public event Action CloseRequested;

        MaidAgent _agent;
        MaidAiChatPanel _panel;
        AiChatClient _client;
        AiChatRecord _record;
        AiChatMessage _activeUser;
        int _loadingBubbleId;
        Coroutine _thinkingRoutine;
        string _thinkingText = "";
        string _status = "";
        bool _busy;
        bool _dirty;
        readonly Queue<FeelingEvent> _feelingQueue = new Queue<FeelingEvent>();

        public MaidAgent Agent
        {
            get { return _agent; }
        }

        public AiChatRecord Record
        {
            get
            {
                EnsureLoaded();
                return _record;
            }
        }

        public bool Busy
        {
            get { return _busy; }
        }

        public bool PanelOpen
        {
            get { return _panel != null && _panel.IsOpen; }
        }

        public string Status
        {
            get { return _status; }
        }

        void Awake()
        {
            _agent = GetComponent<MaidAgent>();
            _client = GetComponent<AiChatClient>();
            if (_client == null) _client = gameObject.AddComponent<AiChatClient>();
            _panel = GetComponent<MaidAiChatPanel>();
            if (_panel == null) _panel = gameObject.AddComponent<MaidAiChatPanel>();
            _panel.Initialize(this);
            _panel.CloseRequested -= OnPanelClose;
            _panel.CloseRequested += OnPanelClose;
            EnsureLoaded();
        }

        void OnEnable()
        {
            GameSettingsStore.Changed -= OnGameSettingsChanged;
            GameSettingsStore.Changed += OnGameSettingsChanged;
            if (!GameSettingsStore.Current.AiEnabled) DisableForGlobalSwitch();
        }

        public void DisableForGlobalSwitch()
        {
            if (_client != null) _client.Cancel();
            _feelingQueue.Clear();
            if (_activeUser != null && _activeUser.Status == "pending")
            {
                _activeUser.Status = "failed";
                _dirty = true;
            }

            _activeUser = null;
            _busy = false;
            StopThinkingRoutine();
            SaveIfDirty();
            HidePanel();
        }

        void OnGameSettingsChanged()
        {
            if (!GameSettingsStore.Current.AiEnabled) DisableForGlobalSwitch();
        }

        public void ShowPanel(Canvas hostCanvas)
        {
            if (!GameSettingsStore.Current.AiEnabled) return;
            EnsureLoaded();
            _panel.Show(hostCanvas);
        }

        public void HidePanel()
        {
            if (_panel != null) _panel.Hide();
        }

        public void SendText(string text)
        {
            if (!GameSettingsStore.Current.AiEnabled)
            {
                SetStatus("AI 功能已关闭");
                return;
            }

            EnsureLoaded();
            text = (text ?? "").Trim();
            if (text.Length == 0 || _busy)
            {
                return;
            }

            GameSettings settings = GameSettingsStore.Current;
            if (string.IsNullOrEmpty(settings.AiApiKey))
            {
                SetStatus("还没有设置 AI Key，请点右上角「配置」");
                return;
            }

            if (string.IsNullOrEmpty(_record.Profile.EffectivePersona))
            {
                if (!settings.AiAutoGeneratePersona)
                {
                    SetStatus("这只女仆没有可用人设。你可以在设置中开启「人设缺失时自动生成」");
                    return;
                }

                GeneratePersonaThenSend(text);
                return;
            }

            SendRequest(text);
        }

        public void TriggerInteractionFeeling(string interactionId, string interactionName)
        {
            if (!GameSettingsStore.Current.AiEnabled) return;
            EnsureLoaded();
            if (_agent == null || _record == null) return;
            if (string.IsNullOrEmpty(GameSettingsStore.Current.AiApiKey)) return;
            if (string.IsNullOrEmpty(_record.Profile.EffectivePersona)) return;

            FeelingEvent item = new FeelingEvent();
            item.Id = interactionId;
            item.Name = interactionName;
            item.EventText = "主人刚刚进入「" + interactionName + "」互动。";
            _feelingQueue.Enqueue(item);
            PumpFeelingQueue();
        }

        public static void ReportPhysicalHit(MaidAgent agent, string kind)
        {
            if (agent == null || !GameSettingsStore.Current.AiEnabled) return;
            MaidAiChatController chat = agent.GetComponent<MaidAiChatController>();
            if (chat == null) chat = agent.gameObject.AddComponent<MaidAiChatController>();
            chat.RegisterPhysicalHit(kind);
        }

        void RegisterPhysicalHit(string kind)
        {
            if (!GameSettingsStore.Current.AiEnabled) return;
            EnsureLoaded();
            if (_record == null) return;

            bool cake = kind == "cake";
            if (cake) _record.CakeHitCount++;
            else _record.SlapHitCount++;
            int count = cake ? _record.CakeHitCount : _record.SlapHitCount;
            _dirty = true;

            if (count % 100 != 0) return;
            SaveNow();
            FeelingEvent item = new FeelingEvent();
            item.Id = cake ? "cake_hit_100" : "slap_hit_100";
            item.Name = cake ? "被蛋糕砸100下" : "被扇脸100下";
            item.EventText = cake
                ? "主人已经累计用蛋糕砸中你" + count + "下。请表达此刻真实的感受。"
                : "主人已经累计扇了你" + count + "次耳光。请表达此刻真实的感受。";
            _feelingQueue.Enqueue(item);
            PumpFeelingQueue();
        }

        public void OpenSettings()
        {
            if (SettingsPanel.Instance != null) SettingsPanel.Instance.Open();
        }

        void OnPanelClose()
        {
            if (CloseRequested != null) CloseRequested();
        }

        public string SiteSummary()
        {
            AiChatSiteInfo site = AiChatSiteCatalog.Get(GameSettingsStore.Current.AiSite);
            return site.DisplayName + " / " + site.Model;
        }

        public string PersonaSummary()
        {
            AiChatProfile profile = Record.Profile;
            if (!string.IsNullOrEmpty(profile.GeneratedPersona)) return "手机端自动生成";
            if (!string.IsNullOrEmpty(profile.ResolvedSystemPrompt)) return "已从 MC 同步";
            return GameSettingsStore.Current.AiAutoGeneratePersona ? "缺失，首次聊天自动生成" : "缺失";
        }

        public void SaveNow()
        {
            if (_record == null || _agent == null || _agent.Save == null) return;
            string error;
            if (AiChatStore.Save(_agent.Save, _record, out error))
            {
                _dirty = false;
            }
            else
            {
                SetStatus("聊天保存失败：" + error);
            }
        }

        void GeneratePersonaThenSend(string text)
        {
            _busy = true;
            SetStatus("正在生成女仆人设…");
            BeginThinking("正在生成人设");
            AiPersonaDescriptor descriptor = new AiPersonaDescriptor();
            descriptor.MaidName = _record.Profile.MaidName;
            descriptor.OwnerName = _record.Profile.OwnerName;
            descriptor.ModelId = _record.Profile.ModelId;
            descriptor.Language = _record.Profile.ChatLanguage;
            AiChatRequest request = MaidAiPromptBuilder.BuildPersona(descriptor);
            _client.Send(request, reply =>
            {
                if (!GameSettingsStore.Current.AiEnabled) return;
                string persona = (reply.Text ?? "").Trim();
                if (persona.Length == 0)
                {
                    Fail("AI 返回了空人设", true);
                    return;
                }

                _record.Profile.GeneratedPersona = persona;
                _record.Profile.PersonaSource = "generated";
                _dirty = true;
                _busy = false;
                EndThinking("人设生成好了", 3f);
                SaveNow();
                NotifyChanged();
                SendText(text);
            }, error =>
            {
                if (!GameSettingsStore.Current.AiEnabled) return;
                Fail("人设生成失败：" + error, true);
            });
        }

        void SendRequest(string text)
        {
            _busy = true;
            string requestId = Guid.NewGuid().ToString("N");
            _activeUser = _record.Append("user", text, "phone", "pending", requestId);
            _dirty = true;
            SaveNow();
            NotifyChanged();
            SetStatus("正在思考…");
            BeginThinking("思考中");

            AiChatRequest request = MaidAiPromptBuilder.BuildChat(_record, text);
            request.ToolsJson = MaidAiExpressionTool.BuildToolsJson(_agent);
            if (!string.IsNullOrEmpty(request.ToolsJson)) request.ToolChoice = "auto";
            SendRound(request, requestId, 0);
        }

        void SendRound(AiChatRequest request, string requestId, int round)
        {
            _client.Send(request, reply =>
            {
                if (!GameSettingsStore.Current.AiEnabled) return;
                if (!string.IsNullOrEmpty(reply.ToolCallsJson))
                {
                    if (round >= 2)
                    {
                        Fail("AI 连续调用工具次数过多，已停止", false);
                        return;
                    }

                    List<AiChatMessage> continuation;
                    string toolStatus;
                    bool started = MaidAiToolExecutor.ExecuteAll(_agent, reply.ToolCallsJson, reply.Text,
                        out continuation, out toolStatus);
                    if (!started)
                    {
                        Fail(toolStatus, false);
                        return;
                    }

                    if (_activeUser != null)
                    {
                        _activeUser.Status = "complete";
                        _dirty = true;
                        SaveNow();
                    }

                    SetStatus(toolStatus + "；正在继续回复…");
                    AiChatRequest next = MaidAiPromptBuilder.BuildChat(_record, "");
                    next.ToolsJson = request.ToolsJson;
                    next.ToolChoice = request.ToolChoice;
                    next.ExtraMessages.AddRange(request.ExtraMessages);
                    next.ExtraMessages.AddRange(continuation);
                    SendRound(next, requestId, round + 1);
                    return;
                }

                if (string.IsNullOrWhiteSpace(reply.Text))
                {
                    Fail("AI 没有返回文字", false);
                    return;
                }

                if (_activeUser != null) _activeUser.Status = "complete";
                AiChatMessage assistant = _record.Append("assistant", reply.Text, "phone", "complete", requestId);
                assistant.TtsText = reply.TtsText ?? "";
                assistant.RawText = string.IsNullOrEmpty(assistant.TtsText)
                    ? assistant.Text
                    : assistant.Text + "---" + assistant.TtsText;
                _dirty = true;
                _busy = false;
                SaveNow();
                EndThinking(reply.Text, MaidChatBubble.DefaultSeconds);
                SetStatus("已回复");
                NotifyChanged();
                PumpFeelingQueue();
            }, error =>
            {
                if (!GameSettingsStore.Current.AiEnabled) return;
                Fail(error, false);
            });
        }

        void PumpFeelingQueue()
        {
            if (!GameSettingsStore.Current.AiEnabled)
            {
                _feelingQueue.Clear();
                return;
            }

            if (_busy || _feelingQueue.Count == 0) return;
            FeelingEvent item = _feelingQueue.Dequeue();
            StartFeelingRequest(item);
        }

        void StartFeelingRequest(FeelingEvent item)
        {
            _busy = true;
            _activeUser = null;
            string requestId = Guid.NewGuid().ToString("N");
            SetStatus("正在感受「" + item.Name + "」…");
            BeginThinking("感受中");
            AiChatRequest request = MaidAiPromptBuilder.BuildPassiveLine(_record, item.EventText);
            _client.Send(request, reply =>
            {
                if (!GameSettingsStore.Current.AiEnabled) return;
                if (string.IsNullOrWhiteSpace(reply.Text) || !string.IsNullOrEmpty(reply.ToolCallsJson))
                {
                    FailFeeling("互动感受生成失败：AI 没有返回文字");
                    return;
                }

                string passiveLine = LimitPassiveLine(reply.Text);
                AiChatMessage assistant = _record.Append("assistant", passiveLine, "phone", "complete", requestId);
                assistant.TtsText = "";
                assistant.RawText = assistant.Text;
                _dirty = true;
                _busy = false;
                SaveNow();
                EndThinking(passiveLine, MaidChatBubble.DefaultSeconds);
                SetStatus("互动感受：" + item.Name);
                NotifyChanged();
                PumpFeelingQueue();
            }, error =>
            {
                if (!GameSettingsStore.Current.AiEnabled) return;
                FailFeeling("互动感受生成失败：" + error);
            });
        }

        static string LimitPassiveLine(string text)
        {
            text = (text ?? "").Trim();
            StringInfo info = new StringInfo(text);
            if (info.LengthInTextElements <= 50) return text;
            return info.SubstringByTextElements(0, 49).TrimEnd() + "…";
        }

        void FailFeeling(string error)
        {
            if (!GameSettingsStore.Current.AiEnabled) return;
            _busy = false;
            EndThinking("……", 3f);
            SetStatus(error);
            NotifyChanged();
            PumpFeelingQueue();
        }

        void Fail(string error, bool personaFailure)
        {
            if (!GameSettingsStore.Current.AiEnabled) return;
            if (_activeUser != null && !personaFailure)
            {
                _activeUser.Status = "failed";
            }

            _dirty = true;
            _busy = false;
            SaveNow();
            EndThinking("连接失败了……", 4f);
            SetStatus(error);
            NotifyChanged();
            PumpFeelingQueue();
        }

        void BeginThinking(string text)
        {
            _thinkingText = text;
            StopThinkingRoutine();
            _loadingBubbleId = MaidChatBubble.ShowPersistent(_agent, text + ".");
            _thinkingRoutine = StartCoroutine(AnimateThinking());
        }

        System.Collections.IEnumerator AnimateThinking()
        {
            int dots = 1;
            while (_loadingBubbleId != 0)
            {
                yield return new WaitForSecondsRealtime(0.45f);
                dots = dots % 3 + 1;
                MaidChatBubble.UpdatePersistent(_agent, _loadingBubbleId,
                    _thinkingText + new string('.', dots));
            }
        }

        void EndThinking(string text, float seconds)
        {
            StopThinkingCoroutine();
            if (_loadingBubbleId != 0)
            {
                MaidChatBubble.Complete(_agent, _loadingBubbleId, text, seconds);
                _loadingBubbleId = 0;
            }
        }

        void StopThinkingRoutine()
        {
            StopThinkingCoroutine();
            if (_loadingBubbleId != 0)
            {
                MaidChatBubble.RemovePersistent(_agent, _loadingBubbleId);
                _loadingBubbleId = 0;
            }
        }

        void StopThinkingCoroutine()
        {
            if (_thinkingRoutine != null)
            {
                StopCoroutine(_thinkingRoutine);
                _thinkingRoutine = null;
            }
        }

        void EnsureLoaded()
        {
            if (_record != null || _agent == null || _agent.Save == null) return;
            string error;
            _record = AiChatStore.Load(_agent.Save, out error);
            if (string.IsNullOrEmpty(_record.MaidUuid)) _record.MaidUuid = _agent.Save.Id;
            if (string.IsNullOrEmpty(_record.Profile.MaidName)) _record.Profile.MaidName = _agent.Save.Name;
            if (string.IsNullOrEmpty(_record.Profile.OwnerName)) _record.Profile.OwnerName = _agent.Save.OwnerName;
            bool recovered = false;
            for (int i = 0; i < _record.Messages.Count; i++)
            {
                if (_record.Messages[i].Status == "pending")
                {
                    _record.Messages[i].Status = "failed";
                    recovered = true;
                }
            }

            if (!string.IsNullOrEmpty(error)) _status = "AI 数据读取失败：" + error;
            if (recovered)
            {
                _dirty = true;
                SaveNow();
            }
        }

        void SetStatus(string status)
        {
            _status = status ?? "";
            NotifyChanged();
        }

        void NotifyChanged()
        {
            if (Changed != null) Changed();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) SaveIfDirty();
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused) SaveIfDirty();
        }

        void OnDisable()
        {
            GameSettingsStore.Changed -= OnGameSettingsChanged;
            if (_client != null) _client.Cancel();
            if (_activeUser != null && _activeUser.Status == "pending")
            {
                _activeUser.Status = "failed";
                _activeUser = null;
                _dirty = true;
            }

            StopThinkingRoutine();
            _busy = false;
            _feelingQueue.Clear();
            SaveIfDirty();
            HidePanel();
        }

        void OnDestroy()
        {
            GameSettingsStore.Changed -= OnGameSettingsChanged;
            if (_client != null) _client.Cancel();
            if (_panel != null) _panel.CloseRequested -= OnPanelClose;
            _feelingQueue.Clear();
            Changed = null;
            CloseRequested = null;
        }

        void SaveIfDirty()
        {
            if (_dirty) SaveNow();
        }
    }
}
