using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 顺毛 / 逆毛的笔画分类器，逐行照抄 moreanimation 的 BrushGrain：
    /// 把"沿着毛发中心线的前进量"累计起来，小幅反向抖动先互相抵消，
    /// 同向攒够一次判定距离才敢定方向；定过方向以后再反向攒够阈值就翻转方向（说明主人换手了）。
    /// 纯数学，不引用相机、屏幕方向或具体骨骼名字，尾巴和头发共用一份。
    /// </summary>
    public sealed class MaidBrushGrain
    {
        public enum Direction
        {
            Unknown,
            WithGrain,
            AgainstGrain
        }

        /// <summary>小于这个的前进量先攒着，正反抖动互相抵消，别把手指哆嗦当划动</summary>
        public const float DeadZone = 0.003f;
        /// <summary>同向累计到这么多才敢判方向</summary>
        public const float ClassifyDistance = 0.12f;
        /// <summary>一个方向要压过另一个方向这么多倍才算数</summary>
        public const float Dominance = 1.4f;
        /// <summary>定过方向后，净前进这么多就算完成一次有效划动</summary>
        public const float PassDistance = 0.18f;
        /// <summary>反向攒到这么多就翻转方向</summary>
        public const float ReverseDistance = ClassifyDistance * 1.5f;
        /// <summary>单次采样超过这么多（投影跳变 / 换部位）直接丢掉</summary>
        public const float MaxSample = 0.35f;

        float _forward;
        float _reverse;
        float _opposite;
        float _pending;
        bool _triggered;

        /// <summary>最近一次采样的前进量（调试用）</summary>
        public float Delta { get; private set; }
        public Direction Current { get; private set; }
        public bool Triggered { get { return _triggered; } }
        public float Forward { get { return _forward; } }
        public float Reverse { get { return _reverse; } }

        public void Reset()
        {
            _forward = 0f;
            _reverse = 0f;
            _opposite = 0f;
            _pending = 0f;
            _triggered = false;
            Delta = 0f;
            Current = Direction.Unknown;
        }

        /// <summary>
        /// 喂一次前进量（正 = 顺着毛的方向）。返回非 Unknown 时表示"刚刚完成一次有效划动"，
        /// 该演一次顺毛 / 逆毛的反馈；同一个方向连续划只报第一次，要 Reset 或换向才会再报。
        /// </summary>
        public Direction Sample(float value)
        {
            Delta = value;
            if (!IsFinite(value) || Mathf.Abs(value) > MaxSample)
            {
                _pending = 0f;
                return Direction.Unknown;
            }

            // 小幅相反抖动相互抵消；缓慢但连续的同向位移仍然能越过死区
            _pending += value;
            if (Mathf.Abs(_pending) < DeadZone)
            {
                return Direction.Unknown;
            }

            value = _pending;
            _pending = 0f;
            if (value > 0f)
            {
                _forward += value;
            }
            else
            {
                _reverse -= value;
            }

            if (Current == Direction.Unknown)
            {
                if (_forward >= ClassifyDistance && _forward >= _reverse * Dominance)
                {
                    Current = Direction.WithGrain;
                }
                else if (_reverse >= ClassifyDistance && _reverse >= _forward * Dominance)
                {
                    Current = Direction.AgainstGrain;
                }
            }
            else
            {
                bool against = Current == Direction.WithGrain ? value < 0f : value > 0f;
                _opposite = Mathf.Max(0f, _opposite + (against ? Mathf.Abs(value) : -Mathf.Abs(value)));
                if (_opposite >= ReverseDistance)
                {
                    Current = Current == Direction.WithGrain ? Direction.AgainstGrain : Direction.WithGrain;
                    _forward = Current == Direction.WithGrain ? _opposite : 0f;
                    _reverse = Current == Direction.AgainstGrain ? _opposite : 0f;
                    _opposite = 0f;
                    _triggered = false;
                }
            }

            float distance = Current == Direction.WithGrain ? _forward - _reverse : _reverse - _forward;
            if (Current != Direction.Unknown && !_triggered && distance >= PassDistance)
            {
                _triggered = true;
                return Current;
            }

            return Direction.Unknown;
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
