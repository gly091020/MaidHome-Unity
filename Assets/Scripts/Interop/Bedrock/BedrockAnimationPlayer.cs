using System.Collections.Generic;
using UnityEngine;

namespace MaidHome.Interop.Bedrock
{
    /// <summary>
    /// 在模型根节点播放烘焙好的基岩动画。
    ///
    /// 用 legacy 的 Animation 组件而不是 Animator：动画是运行时从 JSON 烘出来的，没有
    /// AnimatorController 资源可用，而 Animation + AnimationClip 可以直接在运行时组装。
    /// 主 clip 把「不属于常驻层」的骨骼都写了一遍，所以切换动画不会留下别条动画的姿势。
    ///
    /// pre_parallel（尾巴摆动、长发飘）是常驻层，但不走 Animation 的分层播放：
    /// legacy 的 Play 到底停哪些层、层号算不算数都不好把握，所以这里直接拿烘焙好的采样，
    /// 在 LateUpdate 里按轨道写骨骼；主动画写到的骨骼跳过——次序和 TLM 一致（主动画优先）。
    /// </summary>
    [RequireComponent(typeof(Animation))]
    public sealed class BedrockAnimationPlayer : MonoBehaviour
    {
        [SerializeField] private string _defaultClip = "";
        [SerializeField] private float _speed = 1f;

        struct ParallelTrack
        {
            public Transform Bone;
            public BedrockBoneTrack Track;
            public int Clip;
        }

        readonly List<BedrockClipData> _parallel = new List<BedrockClipData>();
        readonly List<float> _parallelTimes = new List<float>();
        readonly List<ParallelTrack> _parallelTracks = new List<ParallelTrack>();
        readonly Dictionary<string, HashSet<string>> _bonePaths = new Dictionary<string, HashSet<string>>();

        HashSet<string> _mainBones = new HashSet<string>();
        Animation _animation;

        /// <summary>最后 Play 成功的那条 clip 名，摸脸那种"借一段动画演完再换回来"的逻辑要用</summary>
        public string CurrentClipName { get; private set; }

        /// <summary>现在真正在播的 clip 名（可能被外部直接操作 Animation 改过），没有就返回 null</summary>
        public string PlayingClipName
        {
            get
            {
                if (_animation == null)
                {
                    _animation = GetComponent<Animation>();
                }

                if (_animation == null)
                {
                    return null;
                }

                foreach (AnimationState state in _animation)
                {
                    if (_animation.IsPlaying(state.name))
                    {
                        return state.name;
                    }
                }

                return null;
            }
        }

        /// <summary>摸尾巴时尾巴交给弹簧链自己写，这里先关掉，别两边抢同一根骨骼</summary>
        public bool ParallelEnabled
        {
            get { return _parallelEnabled; }
            set { _parallelEnabled = value; }
        }

        bool _parallelEnabled = true;

        void Awake()
        {
            _animation = GetComponent<Animation>();
        }

        void Start()
        {
            if (!string.IsNullOrEmpty(_defaultClip))
            {
                Play(_defaultClip);
            }
        }

        public void SetClips(IList<AnimationClip> clips, IList<BedrockClipData> clipData)
        {
            if (_animation == null)
            {
                _animation = GetComponent<Animation>();
            }

            for (int i = 0; i < clips.Count; i++)
            {
                AnimationClip clip = clips[i];
                if (clip == null || string.IsNullOrEmpty(clip.name))
                {
                    continue;
                }

                if (_animation.GetClip(clip.name) != null)
                {
                    _animation.RemoveClip(clip.name);
                }

                _animation.AddClip(clip, clip.name);
            }

            BuildParallel(clipData);
        }

        /// <summary>
        /// 收集常驻层要驱动的骨骼。烘焙时已经把并行动画削成"只留它自己动过的骨骼"，
        /// 所以这里拿到的每条轨道都是真该写的，不用再判。
        /// </summary>
        void BuildParallel(IList<BedrockClipData> clipData)
        {
            _parallel.Clear();
            _parallelTimes.Clear();
            _parallelTracks.Clear();
            _bonePaths.Clear();
            _mainBones = new HashSet<string>();

            if (clipData == null)
            {
                return;
            }

            for (int i = 0; i < clipData.Count; i++)
            {
                BedrockClipData data = clipData[i];
                HashSet<string> paths = new HashSet<string>();
                for (int t = 0; t < data.Tracks.Count; t++)
                {
                    paths.Add(data.Tracks[t].Path);
                }

                _bonePaths[data.Name] = paths;
                if (!BedrockAnimation.IsParallelName(data.Name))
                {
                    continue;
                }

                int clipIndex = _parallel.Count;
                _parallel.Add(data);
                _parallelTimes.Add(0f);

                for (int t = 0; t < data.Tracks.Count; t++)
                {
                    BedrockBoneTrack track = data.Tracks[t];
                    Transform bone = transform.Find(track.Path);
                    if (bone == null)
                    {
                        continue;
                    }

                    ParallelTrack entry = new ParallelTrack();
                    entry.Bone = bone;
                    entry.Track = track;
                    entry.Clip = clipIndex;
                    _parallelTracks.Add(entry);
                }
            }

            if (_parallel.Count > 0 && _parallelTracks.Count == 0)
            {
                Debug.LogWarning("有 " + _parallel.Count + " 条常驻动画，但骨骼路径一条都没对上，尾巴/头发不会摆", this);
            }
        }

        public bool HasClip(string name)
        {
            return _animation != null && _animation.GetClip(name) != null;
        }

        public bool Play(string name)
        {
            if (_animation == null)
            {
                _animation = GetComponent<Animation>();
            }

            if (_animation == null || _animation.GetClip(name) == null)
            {
                return false;
            }

            AnimationState state = _animation[name];
            state.speed = _speed;
            state.wrapMode = _animation.GetClip(name).wrapMode;
            _animation.Play(name);
            CurrentClipName = name;

            HashSet<string> bones;
            _mainBones = _bonePaths.TryGetValue(name, out bones) ? bones : new HashSet<string>();
            return true;
        }

        /// <summary>clip 长度（秒），没有这条 clip 返回 0</summary>
        public float ClipLength(string name)
        {
            if (_animation == null)
            {
                _animation = GetComponent<Animation>();
            }

            AnimationClip clip = _animation != null ? _animation.GetClip(name) : null;
            return clip != null ? clip.length : 0f;
        }

        /// <summary>当前这条 clip 播到第几秒了（借动画演完要接着原来那口气播，用这个）</summary>
        public float PlayingTime
        {
            get
            {
                string name = PlayingClipName;
                if (name == null)
                {
                    return 0f;
                }

                AnimationState state = _animation[name];
                return state != null ? state.time : 0f;
            }
        }

        /// <summary>Play 之后把进度拨到指定秒数，用于"借了一段动画再换回来"</summary>
        public bool Play(string name, float time)
        {
            if (!Play(name))
            {
                return false;
            }

            AnimationState state = _animation[name];
            if (state != null)
            {
                state.time = Mathf.Max(0f, time);
            }

            return true;
        }

        void LateUpdate()
        {
            if (_parallelTracks.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _parallel.Count; i++)
            {
                BedrockClipData data = _parallel[i];
                float length = Mathf.Max(data.Length, 1f / 30f);
                float time = _parallelTimes[i] + Time.deltaTime * _speed;
                _parallelTimes[i] = data.WrapMode == WrapMode.Loop
                    ? Mathf.Repeat(time, length)
                    : Mathf.Min(time, length);
            }

            if (!_parallelEnabled)
            {
                return;
            }

            for (int i = 0; i < _parallelTracks.Count; i++)
            {
                ParallelTrack entry = _parallelTracks[i];
                if (_mainBones.Contains(entry.Track.Path))
                {
                    // 主动画这几帧自己动这根骨骼，让给它
                    continue;
                }

                Vector3 position;
                Quaternion rotation;
                Vector3 scale;
                Sample(entry.Track, _parallelTimes[entry.Clip], out position, out rotation, out scale);
                entry.Bone.localPosition = position;
                entry.Bone.localRotation = rotation;
                entry.Bone.localScale = scale;
            }
        }

        /// 烘焙好的 track 就是一串等间隔采样，二分找到区间再线性插值
        static void Sample(BedrockBoneTrack track, float time, out Vector3 position, out Quaternion rotation,
            out Vector3 scale)
        {
            int count = track.Count;
            if (count <= 0)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                scale = Vector3.one;
                return;
            }

            if (count == 1 || time <= track.Times[0])
            {
                position = track.Positions[0];
                rotation = track.Rotations[0];
                scale = track.Scales[0];
                return;
            }

            int last = count - 1;
            if (time >= track.Times[last])
            {
                position = track.Positions[last];
                rotation = track.Rotations[last];
                scale = track.Scales[last];
                return;
            }

            int low = 0;
            int high = last;
            while (high - low > 1)
            {
                int mid = (low + high) / 2;
                if (track.Times[mid] <= time)
                {
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }

            float span = track.Times[high] - track.Times[low];
            float t = span > 0.000001f ? (time - track.Times[low]) / span : 0f;
            position = Vector3.Lerp(track.Positions[low], track.Positions[high], t);
            rotation = Quaternion.Slerp(track.Rotations[low], track.Rotations[high], t);
            scale = Vector3.Lerp(track.Scales[low], track.Scales[high], t);
        }

        public void Stop()
        {
            if (_animation != null)
            {
                _animation.Stop();
            }
        }

        public List<string> ClipNames()
        {
            List<string> names = new List<string>();
            if (_animation == null)
            {
                return names;
            }

            foreach (AnimationState state in _animation)
            {
                names.Add(state.name);
            }

            return names;
        }
    }
}
