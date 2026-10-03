using System.Collections.Generic;
using MaidHome.Core.Input;
using MaidHome.Gameplay.Bag;
using MaidHome.Gameplay.Maid;
using UnityEngine;

namespace MaidHome.Gameplay.House
{
    /// <summary>
    /// 看房子时的镜头操作：**单指拖动平移**、**两指捏合缩放**（PC / 编辑器还能用鼠标滚轮），
    /// 状态和数学都放在 HouseCameraFitter 里（本来就是它在管房子取景），这里只负责把手指变成调用。
    ///
    /// 只在"普通看房子"时生效：背包放置、点开女仆、摸脸/摸尾巴这些模式一律让位，
    /// 不然会和它们抢同一根手指（那些模式自己要用拖动）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HouseCameraPanZoom : MonoBehaviour
    {
        [Tooltip("留空就用 HouseCameraFitter 那台相机上的")]
        [SerializeField] private Camera _camera;
        [Tooltip("留空就自己找场景里的 HouseCameraFitter")]
        [SerializeField] private HouseCameraFitter _fitter;
        [Tooltip("留空就自己找（用来判断现在有没有点开女仆）")]
        [SerializeField] private MaidInteractionController _interaction;
        [Tooltip("留空就自己找（放置模式下拖动是瞄准，不能抢）")]
        [SerializeField] private BagPlacementController _placement;

        [Tooltip("手指要移动这么多像素才算拖动/捏合，避免和「点一下」冲突")]
        [SerializeField] private float _dragStartPixels = 14f;
        [Tooltip("捏合灵敏度：手指间距变化 2 倍 = 画面缩放 2 倍（1 就是 1:1）")]
        [SerializeField] private float _pinchSensitivity = 1f;
        [Tooltip("鼠标滚轮一格缩放多少（PC / 编辑器用）")]
        [SerializeField] private float _wheelStep = 0.12f;

        readonly List<PointerInput.Pointer> _pointers = new List<PointerInput.Pointer>();
        Vector2 _gestureOrigin;
        Vector2 _lastMid;
        float _pinchOrigin;
        float _lastDistance;
        int _lastCount;
        bool _moving;

        void Awake()
        {
            if (_camera == null)
            {
                _camera = GetComponent<Camera>();
            }

            if (_fitter == null && _camera != null)
            {
                _fitter = _camera.GetComponent<HouseCameraFitter>();
            }

            if (_fitter == null)
            {
                _fitter = FindObjectOfType<HouseCameraFitter>();
            }

            if (_interaction == null)
            {
                _interaction = FindObjectOfType<MaidInteractionController>();
            }

            if (_placement == null)
            {
                _placement = FindObjectOfType<BagPlacementController>();
            }
        }

        /// <summary>回到房子的原始取景，给 UI 上的「复位视角」按钮用</summary>
        public void ResetView()
        {
            if (_fitter != null)
            {
                _fitter.ResetPanZoom();
            }
        }

        void Update()
        {
            if (Blocked())
            {
                ResetGesture();
                return;
            }

            WheelZoom();

            PointerInput.CopyPointers(_pointers);
            int count = 0;
            Vector2 mid = Vector2.zero;
            Vector2 first = Vector2.zero;
            float distance = 0f;
            for (int i = 0; i < _pointers.Count; i++)
            {
                PointerInput.Pointer pointer = _pointers[i];
                if (!pointer.Held || pointer.OverUi)
                {
                    continue;
                }

                count++;
                mid += pointer.Position;
                if (count == 1)
                {
                    first = pointer.Position;
                }
                else if (count == 2)
                {
                    distance = (pointer.Position - first).magnitude;
                }
            }

            if (count == 0)
            {
                ResetGesture();
                return;
            }

            mid /= count;
            if (_lastCount != count)
            {
                // 手指加减了（一根变两根）：重新定基准，别把这次跳变当成拖动
                _lastCount = count;
                _gestureOrigin = mid;
                _pinchOrigin = distance;
                _lastMid = mid;
                _lastDistance = distance;
                return;
            }

            if (!_moving)
            {
                float travel = (mid - _gestureOrigin).magnitude;
                float pinch = distance > 1f && _pinchOrigin > 1f
                    ? Mathf.Abs(distance - _pinchOrigin)
                    : 0f;
                if (travel <= _dragStartPixels && pinch <= _dragStartPixels)
                {
                    return;
                }

                // 超过阈值才开始，而且从"现在"算起，不然起手会跳一下
                _moving = true;
                _lastMid = mid;
                _lastDistance = distance;
            }

            if (_lastDistance > 1f && distance > 1f)
            {
                // 传下去的是"正交尺寸的倍率"：尺寸越小画面越大。
                // 手指分开（distance 变大）= 拉近放大，所以倍率要取反（和滚轮往上滚 = 放大 保持一致）
                float factor = Mathf.Pow(_lastDistance / distance, Mathf.Max(0.05f, _pinchSensitivity));
                _fitter.ZoomBy(factor, mid);
            }

            _fitter.PanByPixels(mid - _lastMid);
            _lastMid = mid;
            _lastDistance = distance;
        }

        void WheelZoom()
        {
            float scroll = PointerInput.ScrollDelta;
            if (Mathf.Abs(scroll) < 0.0001f)
            {
                return;
            }

            // 往上滚 = 拉近（正交尺寸变小）
            _fitter.ZoomBy(1f - scroll * _wheelStep, PointerInput.MousePosition);
        }

        /// <summary>这几个模式自己要用拖动，或者相机的取景现在归别人管</summary>
        bool Blocked()
        {
            if (_fitter == null || !_fitter.CanPanZoom)
            {
                return true;
            }

            if (_placement != null && _placement.IsActive)
            {
                return true;
            }

            return _interaction != null && _interaction.IsFocused;
        }

        void ResetGesture()
        {
            _moving = false;
            _lastCount = 0;
            _lastDistance = 0f;
            _pinchOrigin = 0f;
        }
    }
}
