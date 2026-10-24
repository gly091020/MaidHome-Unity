using MaidHome.Interop.Bedrock;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    [DisallowMultipleComponent]
    public sealed class MaidAiExpressionPlayer : MonoBehaviour
    {
        BedrockAnimationPlayer _player;
        string _activeClip = "";
        string _restoreClip = "";
        float _restoreAt;

        public bool PlayClip(string clipName, out string error)
        {
            error = "";
            if (string.IsNullOrEmpty(clipName))
            {
                error = "没有指定动画";
                return false;
            }

            if (_player == null) _player = GetComponent<BedrockAnimationPlayer>();
            if (_player == null || !_player.HasClip(clipName))
            {
                error = "当前女仆没有动画：" + clipName;
                return false;
            }

            string playing = _player.PlayingClipName;
            if (string.IsNullOrEmpty(_activeClip) || playing != _activeClip)
            {
                _restoreClip = playing;
                if (string.IsNullOrEmpty(_restoreClip) && _player.HasClip("idle"))
                {
                    _restoreClip = "idle";
                }
            }

            if (!_player.PlayBlended(clipName, 0.12f))
            {
                error = "动画播放失败：" + clipName;
                return false;
            }

            _activeClip = clipName;
            float duration = Mathf.Clamp(_player.ClipLength(clipName), 0.8f, 5f);
            _restoreAt = Time.time + duration;
            return true;
        }

        void Update()
        {
            if (string.IsNullOrEmpty(_activeClip) || Time.time < _restoreAt) return;
            string finished = _activeClip;
            _activeClip = "";
            if (_player == null || !_player.IsPlayingClip(finished)) return;

            if (!string.IsNullOrEmpty(_restoreClip))
            {
                _player.PlayBlended(_restoreClip, 0.15f);
            }
            else
            {
                _player.StopClip(finished);
            }
        }
    }
}
