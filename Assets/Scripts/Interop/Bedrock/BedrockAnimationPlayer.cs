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

        /// <summary>一条动画：采样数据一直在，AnimationClip 等第一次播到它才建（见 EnsureClip）</summary>
        sealed class ClipEntry
        {
            public string Name;
            public BedrockClipData Data;
            public AnimationClip Clip;
        }

        readonly List<BedrockClipData> _parallel = new List<BedrockClipData>();
        readonly List<float> _parallelTimes = new List<float>();
        readonly List<ParallelTrack> _parallelTracks = new List<ParallelTrack>();
        readonly List<ClipEntry> _entries = new List<ClipEntry>();
        readonly Dictionary<string, ClipEntry> _entryByName = new Dictionary<string, ClipEntry>();
        readonly List<AnimationClip> _ownedClips = new List<AnimationClip>();

        HashSet<string> _mainBones = new HashSet<string>();
        Animation _animation;
        IList<BedrockClipData> _clipData;

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

        /// <summary>
        /// 只记下动画的采样数据，**不建 AnimationClip**。
        /// 一条 10 秒的动画要写几百条曲线，一次全建出来 SetCurve 就是几万个调用（一百多条动画
        /// 有三十多万个），进游戏会卡好几秒；真正的 clip 等第一次播到它时再建（EnsureClip）。
        /// clips 里带了现成的 clip 就用现成的（编辑器工具会这么传），名字对不上再按需烘。
        /// </summary>
        public void SetClips(IList<AnimationClip> clips, IList<BedrockClipData> clipData)
        {
            // 收回背包再拿出来时会再调一次，数据没换就别动：重加 clip 会把正在播的动画掐掉
            if (ReferenceEquals(_clipData, clipData) && _entries.Count > 0)
            {
                return;
            }

            if (_animation == null)
            {
                _animation = GetComponent<Animation>();
            }

            for (int i = 0; i < _entries.Count; i++)
            {
                ClipEntry old = _entries[i];
                if (old.Clip != null && _animation != null && _animation.GetClip(old.Clip.name) != null)
                {
                    _animation.RemoveClip(old.Clip.name);
                }
            }

            ReleaseOwnedClips();
            _entries.Clear();
            _entryByName.Clear();
            _clipData = clipData;

            if (clips != null)
            {
                for (int i = 0; i < clips.Count; i++)
                {
                    AnimationClip clip = clips[i];
                    if (clip == null || string.IsNullOrEmpty(clip.name))
                    {
                        continue;
                    }

                    ClipEntry entry = FindOrAdd(clip.name);
                    entry.Clip = clip;
                    if (_animation != null && _animation.GetClip(clip.name) == null)
                    {
                        _animation.AddClip(clip, clip.name);
                    }
                }
            }

            if (clipData != null)
            {
                for (int i = 0; i < clipData.Count; i++)
                {
                    BedrockClipData data = clipData[i];
                    if (data == null || string.IsNullOrEmpty(data.Name))
                    {
                        continue;
                    }

                    FindOrAdd(data.Name).Data = data;
                }
            }

            BuildParallel(clipData);
        }

        ClipEntry FindOrAdd(string name)
        {
            ClipEntry entry;
            if (_entryByName.TryGetValue(name, out entry))
            {
                return entry;
            }

            entry = new ClipEntry();
            entry.Name = name;
            _entries.Add(entry);
            _entryByName.Add(name, entry);
            return entry;
        }

        /// <summary>第一次播到这条动画时才烘出 AnimationClip，之后一直复用。</summary>
        public AnimationClip EnsureClip(string name)
        {
            ClipEntry entry;
            return !string.IsNullOrEmpty(name) && _entryByName.TryGetValue(name, out entry)
                ? EnsureClip(entry)
                : null;
        }

        AnimationClip EnsureClip(ClipEntry entry)
        {
            if (entry.Clip != null || entry.Data == null)
            {
                return entry.Clip;
            }

            // 采样可能还在缓存文件里，烘之前先读出来
            entry.Data.EnsureTracks();
            if (_animation == null)
            {
                _animation = GetComponent<Animation>();
            }

            BedrockAnimationClipBuilder builder = new BedrockAnimationClipBuilder();
            builder.SampleRate = BedrockAnimationClipBuilder.DefaultSampleRate;
            entry.Clip = builder.BuildClip(entry.Data);
            _ownedClips.Add(entry.Clip);
            if (_animation != null && !string.IsNullOrEmpty(entry.Clip.name)
                && _animation.GetClip(entry.Clip.name) == null)
            {
                _animation.AddClip(entry.Clip, entry.Clip.name);
            }

            return entry.Clip;
        }

        void ReleaseOwnedClips()
        {
            for (int i = 0; i < _ownedClips.Count; i++)
            {
                if (_ownedClips[i] != null)
                {
                    Destroy(_ownedClips[i]);
                }
            }

            _ownedClips.Clear();
        }

        void OnDestroy()
        {
            ReleaseOwnedClips();
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
            _mainBones = new HashSet<string>();

            if (clipData == null)
            {
                return;
            }

            for (int i = 0; i < clipData.Count; i++)
            {
                BedrockClipData data = clipData[i];
                if (!BedrockAnimation.IsParallelName(data.Name))
                {
                    continue;
                }

                // 常驻层每帧都要用采样，就地读出来（只有 pre_parallel 那几条，别的动画等播到再说）
                data.EnsureTracks();
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
            return !string.IsNullOrEmpty(name) && _entryByName.ContainsKey(name);
        }

        public bool Play(string name)
        {
            if (_animation == null)
            {
                _animation = GetComponent<Animation>();
            }

            ClipEntry entry;
            if (_animation == null || !_entryByName.TryGetValue(name, out entry))
            {
                return false;
            }

            if (EnsureClip(entry) == null || _animation.GetClip(name) == null)
            {
                return false;
            }

            AnimationState state = _animation[name];
            state.speed = _speed;
            state.wrapMode = _animation.GetClip(name).wrapMode;
            _animation.Play(name);
            CurrentClipName = name;

            // 主动画写到的骨骼（EnsureClip 刚保证了采样已经读出来）
            _mainBones = new HashSet<string>();
            for (int t = 0; t < entry.Data.Tracks.Count; t++)
            {
                _mainBones.Add(entry.Data.Tracks[t].Path);
            }

            return true;
        }

        /// <summary>clip 长度（秒），没有这条 clip 返回 0</summary>
        public float ClipLength(string name)
        {
            ClipEntry entry;
            if (string.IsNullOrEmpty(name) || !_entryByName.TryGetValue(name, out entry))
            {
                return 0f;
            }

            // 没建过 clip 也能答：采样数据里的 length 就是烘出来那条 clip 的长度
            return entry.Clip != null ? entry.Clip.length : entry.Data != null ? entry.Data.Length : 0f;
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
            List<string> names = new List<string>(_entries.Count);
            for (int i = 0; i < _entries.Count; i++)
            {
                names.Add(_entries[i].Name);
            }

            return names;
        }
    }
}
