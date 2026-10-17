using System.Collections.Generic;
using MaidHome.Interop.Bedrock;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 女仆自己的受伤动画层：每挨一下加 5 秒，连续挨打会一直累加（最多 60 秒）。
    /// 计时和动画都挂在女仆身上，退出摸脸 / 喂蛋糕 / 玩全身后仍然继续。
    /// 识别到头时只覆盖 head 子树；没有头才允许整身播放。
    /// 优先级：sosad → game_lost2 → game_lost。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public sealed class MaidHurtAnimation : MonoBehaviour
    {
        public const float DefaultHitSeconds = 5f;
        public const float DefaultMaxSeconds = 60f;

        [Tooltip("每受一次伤给受伤动画加多少秒")]
        [SerializeField] private float _secondsPerHit = DefaultHitSeconds;
        [Tooltip("连续受伤最多累计到多少秒")]
        [SerializeField] private float _maxSeconds = DefaultMaxSeconds;
        [Tooltip("sosad / game_lost2 都没有时的最后备用动画")]
        [SerializeField] private string _fallbackClip = "game_lost";
        [SerializeField] private float _fullBodyFadeSeconds = 0.12f;

        readonly List<BedrockBoneTrack> _tracks = new List<BedrockBoneTrack>();
        readonly List<Transform> _bones = new List<Transform>();
        readonly List<Vector3> _restPositions = new List<Vector3>();
        readonly List<Quaternion> _restRotations = new List<Quaternion>();
        readonly List<Vector3> _restScales = new List<Vector3>();

        BedrockAnimationPlayer _player;
        Transform _head;
        string _clip;
        string _returnClip;
        float _returnTime;
        bool _returnRecorded;
        bool _active;
        bool _partial;
        bool _headHadBody;
        float _remaining;
        float _time;
        float _length;

        public bool Active
        {
            get { return _active; }
        }

        public float Remaining
        {
            get { return _active ? _remaining : 0f; }
        }

        void LateUpdate()
        {
            if (!_active)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0.000001f)
            {
                return;
            }

            if (_player == null)
            {
                FinishNow();
                return;
            }

            if (_partial)
            {
                if (_head == null)
                {
                    FinishNow();
                    return;
                }

                // 布娃娃退出后刚体会被拆掉，重新记一次这一帧的基准姿势，别把躺姿当成站立基准
                bool hasBody = _head.GetComponent<Rigidbody>() != null;
                if (_headHadBody && !hasBody)
                {
                    RebaseRest();
                }

                _headHadBody = hasBody;
                ApplyPartial(deltaTime);
            }
            else if (!_player.IsPlayingClip(_clip))
            {
                // 游走 / 吃东西 / 别的玩法把受伤动画顶掉时，在计时结束前一直顶回来
                _player.Play(_clip);
            }

            _remaining -= deltaTime;
            if (_remaining <= 0f)
            {
                FinishNow();
            }
        }

        public void AddHit(bool allowFullBody, Transform head)
        {
            if (_active)
            {
                _remaining = Mathf.Min(MaxSeconds, _remaining + HitSeconds);
                return;
            }

            Begin(allowFullBody, head);
        }

        public void StopNow()
        {
            FinishNow();
        }

        [ContextMenu("受伤加 5 秒")]
        void TestHit()
        {
            AddHit(true, null);
        }

        [ContextMenu("停止受伤动画")]
        void TestStop()
        {
            StopNow();
        }

        void Begin(bool allowFullBody, Transform requestedHead)
        {
            _player = GetComponent<BedrockAnimationPlayer>();
            if (_player == null)
            {
                return;
            }

            Transform head = requestedHead;
            if (head == null)
            {
                MaidFaceRig rig = MaidFaceRig.Build(transform);
                head = rig != null ? rig.Head : null;
            }

            string[] priority = { "sosad", "game_lost2", _fallbackClip };
            for (int i = 0; i < priority.Length; i++)
            {
                string clip = priority[i];
                if (string.IsNullOrEmpty(clip) || !_player.HasClip(clip))
                {
                    continue;
                }

                if (head != null)
                {
                    if (TryBeginPartial(clip, head))
                    {
                        return;
                    }

                    continue;
                }

                if (allowFullBody)
                {
                    BeginFullBody(clip);
                    return;
                }

                return;
            }
        }

        bool TryBeginPartial(string clip, Transform head)
        {
            List<BedrockBoneTrack> tracks = new List<BedrockBoneTrack>();
            List<Transform> bones = new List<Transform>();
            if (!_player.TryGetTracksUnder(clip, head, tracks, bones))
            {
                return false;
            }

            _tracks.Clear();
            _bones.Clear();
            _restPositions.Clear();
            _restRotations.Clear();
            _restScales.Clear();
            for (int i = 0; i < tracks.Count; i++)
            {
                Transform bone = bones[i];
                // 布娃娃里除了头根之外的其他刚体块交给物理，别和关节抢
                if (bone == null || (bone != head && bone.GetComponent<Rigidbody>() != null))
                {
                    continue;
                }

                _tracks.Add(tracks[i]);
                _bones.Add(bone);
                _restPositions.Add(bone.localPosition);
                _restRotations.Add(bone.localRotation);
                _restScales.Add(bone.localScale);
            }

            if (_tracks.Count == 0)
            {
                return false;
            }

            _head = head;
            _partial = true;
            _headHadBody = head.GetComponent<Rigidbody>() != null;
            _clip = clip;
            _length = Mathf.Max(0.01f, _player.ClipLength(clip));
            _time = 0f;
            _remaining = Mathf.Min(MaxSeconds, HitSeconds);
            _returnClip = null;
            _returnTime = 0f;
            _returnRecorded = false;
            _active = true;
            return true;
        }

        void BeginFullBody(string clip)
        {
            _partial = false;
            _head = null;
            _clip = clip;
            _tracks.Clear();
            _bones.Clear();
            _restPositions.Clear();
            _restRotations.Clear();
            _restScales.Clear();
            _length = Mathf.Max(0.01f, _player.ClipLength(clip));
            _time = 0f;
            _remaining = Mathf.Min(MaxSeconds, HitSeconds);
            _returnClip = _player.PlayingClipName;
            _returnTime = _player.PlayingTime;
            _returnRecorded = !string.IsNullOrEmpty(_returnClip) && _returnClip != clip;
            if (!_player.PlayBlended(clip, _fullBodyFadeSeconds))
            {
                _returnClip = null;
                _returnTime = 0f;
                _returnRecorded = false;
                return;
            }

            _active = true;
        }

        float HitSeconds
        {
            get { return Mathf.Max(0.05f, _secondsPerHit <= 0f ? DefaultHitSeconds : _secondsPerHit); }
        }

        float MaxSeconds
        {
            get
            {
                float value = _maxSeconds <= 0f ? DefaultMaxSeconds : _maxSeconds;
                return Mathf.Clamp(value, 1f, DefaultMaxSeconds);
            }
        }

        void ApplyPartial(float deltaTime)
        {
            _time += deltaTime;
            float time = _length > 0.001f ? Mathf.Repeat(_time, _length) : _time;
            Rigidbody headBody = _head != null ? _head.GetComponent<Rigidbody>() : null;
            for (int i = 0; i < _tracks.Count; i++)
            {
                Transform bone = _bones[i];
                if (bone == null)
                {
                    continue;
                }

                Vector3 position;
                Quaternion rotation;
                Vector3 scale;
                BedrockAnimationPlayer.Sample(_tracks[i], time, out position, out rotation, out scale);
                if (bone == _head && headBody != null)
                {
                    Transform parent = bone.parent;
                    headBody.MoveRotation(parent != null ? parent.rotation * rotation : rotation);
                }
                else
                {
                    bone.localPosition = position;
                    bone.localRotation = rotation;
                    bone.localScale = scale;
                }
            }
        }

        void RebaseRest()
        {
            for (int i = 0; i < _bones.Count; i++)
            {
                Transform bone = _bones[i];
                if (bone == null)
                {
                    continue;
                }

                _restPositions[i] = bone.localPosition;
                _restRotations[i] = bone.localRotation;
                _restScales[i] = bone.localScale;
            }
        }

        void FinishNow()
        {
            if (!_active)
            {
                return;
            }

            _active = false;
            if (_partial)
            {
                RestorePartial();
            }
            else
            {
                ReturnFromFullBody();
            }

            _tracks.Clear();
            _bones.Clear();
            _restPositions.Clear();
            _restRotations.Clear();
            _restScales.Clear();
            _head = null;
            _clip = null;
            _returnClip = null;
            _returnTime = 0f;
            _returnRecorded = false;
            _remaining = 0f;
            _time = 0f;
            _length = 0f;
        }

        void RestorePartial()
        {
            for (int i = 0; i < _bones.Count; i++)
            {
                Transform bone = _bones[i];
                if (bone == null)
                {
                    continue;
                }

                // 头根还在布娃娃里时交给物理；退出布娃娃后下一次会按正常骨骼写回
                if (bone == _head && bone.GetComponent<Rigidbody>() != null)
                {
                    continue;
                }

                if (bone == _head)
                {
                    MaidSimpleBedrockAnimator simple = GetComponent<MaidSimpleBedrockAnimator>();
                    if (simple != null && simple.isActiveAndEnabled)
                    {
                        simple.RestoreHeadPose();
                        continue;
                    }
                }

                bone.localPosition = _restPositions[i];
                bone.localRotation = _restRotations[i];
                bone.localScale = _restScales[i];
            }
        }

        void ReturnFromFullBody()
        {
            if (_player != null && _returnRecorded && !string.IsNullOrEmpty(_returnClip)
                && _player.HasClip(_returnClip))
            {
                _player.PlayBlended(_returnClip, _returnTime, _fullBodyFadeSeconds);
                return;
            }

            MaidWanderer wanderer = GetComponent<MaidWanderer>();
            if (wanderer != null)
            {
                wanderer.ReplayAnimation();
            }
        }

        /// <summary>喂蛋糕退出时清掉"回 beg"的记录，否则受伤演完会把祈求姿势又播回来</summary>
        public void ForgetReturnClip()
        {
            _returnClip = null;
            _returnTime = 0f;
            _returnRecorded = false;
        }

        public static MaidHurtAnimation Find(MaidAgent agent)
        {
            return agent != null ? agent.GetComponent<MaidHurtAnimation>() : null;
        }

        public static MaidHurtAnimation Ensure(MaidAgent agent)
        {
            if (agent == null)
            {
                return null;
            }

            MaidHurtAnimation hurt = agent.GetComponent<MaidHurtAnimation>();
            if (hurt == null)
            {
                hurt = agent.gameObject.AddComponent<MaidHurtAnimation>();
            }

            return hurt;
        }

        public static bool IsPlaying(MaidAgent agent)
        {
            MaidHurtAnimation hurt = Find(agent);
            return hurt != null && hurt._active;
        }

        /// <summary>一次受伤：计时累加，最多一分钟；识别到头时只动 head 子树</summary>
        public static void Hurt(MaidAgent agent, bool allowFullBody = true, Transform head = null)
        {
            MaidHurtAnimation hurt = Ensure(agent);
            if (hurt != null)
            {
                hurt.AddHit(allowFullBody, head);
            }
        }

        public static void ForgetReturn(MaidAgent agent)
        {
            MaidHurtAnimation hurt = Find(agent);
            if (hurt != null)
            {
                hurt.ForgetReturnClip();
            }
        }
    }
}
