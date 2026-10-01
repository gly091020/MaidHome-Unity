using System.Collections.Generic;
using UnityEngine;

namespace MaidHome.Interop.Bedrock
{
    /// <summary>
    /// 在模型根节点播放烘焙好的基岩动画。
    ///
    /// 用 legacy 的 Animation 组件而不是 Animator：动画是运行时从 JSON 烘出来的，没有
    /// AnimatorController 资源可用，而 Animation + AnimationClip 可以直接在运行时组装。
    /// 每个 clip 都把所有骨骼写了一遍，所以切换动画不会留下别的动画的姿势。
    /// </summary>
    [RequireComponent(typeof(Animation))]
    public sealed class BedrockAnimationPlayer : MonoBehaviour
    {
        [SerializeField] private string _defaultClip = "";
        [SerializeField] private float _speed = 1f;

        Animation _animation;

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

        public void SetClips(IList<AnimationClip> clips)
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
            return true;
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
