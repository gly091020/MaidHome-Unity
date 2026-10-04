using System;
using System.Collections.Generic;
using MaidHome.Core.Input;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace MaidHome.Gameplay.Ar
{
    /// <summary>
    /// AR 里的放置与手势：点地面定位置、单指拖动挪位置、双指捏合改大小。
    /// 只管摆 transform，模型从哪来是 MaidArSession 的事。
    ///
    /// 手指只有触摸一套（走 PointerInput），压在 UI 上起手的手势不算。
    /// 转身不做手势，交给面板按钮——手指不够用，转起来也抖。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidArPlacement : MonoBehaviour
    {
        [Tooltip("留空就找场景里的 ARRaycastManager")]
        [SerializeField] private ARRaycastManager _raycast;
        [Tooltip("留空就用 Camera.main（AR 相机必须挂 MainCamera）")]
        [SerializeField] private Camera _camera;
        [Tooltip("还没放下时跟着手指走的落点提示，留空就自动生成一个半透明圆盘")]
        [SerializeField] private GameObject _indicator;
        [Tooltip("自动生成的落点圆盘多大（米）")]
        [SerializeField] private float _indicatorDiameter = 0.5f;
        [Tooltip("女仆放下去时的默认大小：1 = 和游戏里一样大，0.5 = 一半（手办感）")]
        [SerializeField] private float _defaultScale = 0.5f;
        [Tooltip("挪这么多像素才算拖动，免得点一下就把她挪走")]
        [SerializeField] private float _dragThresholdPixels = 14f;
        [SerializeField] private float _minScale = 0.1f;
        [SerializeField] private float _maxScale = 1.5f;

        /// <summary>点在平面上、而手上还没有女仆：交给 MaidArSession 去加载再放。</summary>
        public event Action<Pose> PlaneTapped;

        /// <summary>点了屏幕却没打到平面（多半是还没识别出地面）。交给 MaidArSession 提示玩家。</summary>
        public event Action PlaneMissed;

        readonly List<ARRaycastHit> _hits = new List<ARRaycastHit>();
        readonly List<PointerInput.Pointer> _pointers = new List<PointerInput.Pointer>();

        Transform _target;
        float _baseScale = 1f;
        float _scale = 1f;
        float _heightOffset;
        float _yaw;
        Quaternion _tilt = Quaternion.identity;
        float _pinchBaseDistance;
        float _pinchBaseScale;
        Vector2 _pressPosition;
        int _dragFinger = -1;
        bool _dragging;
        bool _gestureHandled;
        bool _hasLastPose;
        Pose _lastPose;

        public bool HasTarget
        {
            get { return _target != null; }
        }

        /// <summary>当前大小倍率（1 = 和游戏里一样大）</summary>
        public float Scale
        {
            get { return _scale; }
        }

        public float MinScale
        {
            get { return _minScale; }
        }

        public float MaxScale
        {
            get { return _maxScale; }
        }

        void Awake()
        {
            Resolve();
        }

        void Resolve()
        {
            if (_camera == null)
            {
                _camera = Camera.main;
            }

            if (_raycast == null)
            {
                _raycast = FindObjectOfType<ARRaycastManager>();
            }

            if (_raycast == null)
            {
                Debug.LogWarning("场景里没有 ARRaycastManager，AR 没法往平面上放东西", this);
            }

            if (_indicator == null)
            {
                _indicator = CreateIndicator();
            }
        }

        void Update()
        {
            if (_camera == null || _raycast == null)
            {
                return;
            }

            if (HandlePinch())
            {
                _dragFinger = -1;
                _dragging = false;
                return;
            }

            PointerInput.Pointer primary = PointerInput.Primary;

            // 抬起那一帧 PointerInput 里 Held 是 false，所以要先处理 Released
            if (primary.Released)
            {
                HandleRelease(primary);
                return;
            }

            if (!primary.Held)
            {
                ResetDrag();
                ShowIndicator(false, default(Pose));
                return;
            }

            if (primary.OverUi)
            {
                return;
            }

            if (primary.Pressed)
            {
                _dragFinger = primary.FingerId;
                _pressPosition = primary.Position;
                _dragging = false;
                _gestureHandled = false;
            }
            else if (primary.FingerId != _dragFinger)
            {
                // 换了一根手指：这段手势不接着上一段算
                return;
            }

            if (!_dragging
                && (primary.Position - _pressPosition).sqrMagnitude
                    > _dragThresholdPixels * _dragThresholdPixels)
            {
                _dragging = true;
            }

            Pose pose;
            bool hit = RaycastPlane(primary.Position, out pose);
            ShowIndicator(_target == null && hit, pose);

            if (_target == null)
            {
                // 按下那一帧没打到不要紧：手指还按着的时候哪一帧打到了就放
                if (hit && !_gestureHandled)
                {
                    _gestureHandled = true;
                    if (PlaneTapped != null)
                    {
                        PlaneTapped(pose);
                    }
                }
                else if (!hit && primary.Pressed && PlaneMissed != null)
                {
                    PlaneMissed();
                }

                return;
            }

            if (_dragging && hit)
            {
                PlaceAt(pose, false);
                _gestureHandled = true;
            }
        }

        /// <summary>抬手：这一段既没拖动、也没放过东西的话，就当"轻点"——已经放下的一只挪过去。</summary>
        void HandleRelease(PointerInput.Pointer primary)
        {
            bool dragged = _dragging;
            bool handled = _gestureHandled;
            bool sameFinger = primary.FingerId == _dragFinger;
            ResetDrag();

            if (primary.OverUi || dragged || handled || !sameFinger)
            {
                ShowIndicator(false, default(Pose));
                return;
            }

            Pose pose;
            if (RaycastPlane(primary.Position, out pose))
            {
                if (_target == null)
                {
                    if (PlaneTapped != null)
                    {
                        PlaneTapped(pose);
                    }
                }
                else
                {
                    PlaceAt(pose, false);
                }
            }
            else if (_target == null && PlaneMissed != null)
            {
                PlaneMissed();
            }

            ShowIndicator(false, default(Pose));
        }

        void ResetDrag()
        {
            _dragFinger = -1;
            _dragging = false;
            _gestureHandled = false;
        }

        /// <summary>接管一只已经建好的女仆。</summary>
        public void Attach(Transform target)
        {
            _target = target;
            _dragging = false;
            _dragFinger = -1;
            _tilt = Quaternion.identity;
            _yaw = _target != null ? _target.eulerAngles.y : 0f;

            // 模型自己带一个比例（maid.json 里的 scale，酒狐是 0.65）：AR 里的倍率是相对它算的，
            // 这样 "1" 就是和游戏里一样大，而不是把模型自带的比例直接抹掉
            _baseScale = _target != null ? Mathf.Max(0.0001f, _target.localScale.x) : 1f;
            _scale = Mathf.Clamp(_defaultScale, _minScale, _maxScale);

            if (_target != null)
            {
                ApplyScale(_scale);
            }
        }

        public void Detach()
        {
            _target = null;
            _hasLastPose = false;
            _tilt = Quaternion.identity;
            ShowIndicator(false, default(Pose));
        }

        /// <summary>摆到平面上的某一点（女仆的根在脚底，正好站在平面上）。</summary>
        public void PlaceAt(Pose pose, bool faceCamera)
        {
            if (_target == null)
            {
                return;
            }

            _lastPose = pose;
            _hasLastPose = true;
            // 高度补偿按模型自己的尺度算：缩放改的是根节点，这里乘以当前缩放才不会被放大/缩小带偏
            _target.position = pose.position
                + Vector3.up * (_heightOffset * Mathf.Abs(_target.localScale.y));

            if (faceCamera)
            {
                FaceCamera();
            }

            ShowIndicator(false, pose);
        }

        /// <summary>整体抬高/压低（坐姿要对着模型自己的 sit 姿势微调）。</summary>
        public void SetHeightOffset(float meters)
        {
            _heightOffset = meters;
            if (_target != null && _hasLastPose)
            {
                PlaceAt(_lastPose, false);
            }
        }

        public void Rotate(float degrees)
        {
            if (_target == null)
            {
                return;
            }

            _yaw += degrees;
            ApplyRotation();
        }

        /// <summary>姿势要求根节点额外带的倾斜（躺下），绕自身轴，不影响朝向</summary>
        public void SetTilt(Quaternion tilt)
        {
            _tilt = tilt;
            ApplyRotation();
        }

        public void ApplyScale(float scale)
        {
            _scale = Mathf.Clamp(scale, _minScale, _maxScale);
            if (_target != null)
            {
                _target.localScale = Vector3.one * (_baseScale * _scale);
            }
        }

        /// <summary>两指捏合。返回 true 表示这一帧的手势已经被捏合吃掉了。</summary>
        bool HandlePinch()
        {
            PointerInput.CopyPointers(_pointers);

            int first = -1;
            int second = -1;
            for (int i = 0; i < _pointers.Count; i++)
            {
                if (!_pointers[i].Held || _pointers[i].OverUi)
                {
                    continue;
                }

                if (first < 0)
                {
                    first = i;
                }
                else
                {
                    second = i;
                    break;
                }
            }

            if (_target == null || first < 0 || second < 0)
            {
                _pinchBaseDistance = 0f;
                return false;
            }

            float distance = Vector2.Distance(_pointers[first].Position, _pointers[second].Position);
            if (_pinchBaseDistance <= 0.01f)
            {
                _pinchBaseDistance = distance;
                _pinchBaseScale = _scale;
                return true;
            }

            if (distance > 1f)
            {
                ApplyScale(_pinchBaseScale * distance / _pinchBaseDistance);
            }

            return true;
        }

        bool RaycastPlane(Vector2 screenPosition, out Pose pose)
        {
            _hits.Clear();
            if (_raycast.Raycast(screenPosition, _hits, TrackableType.PlaneWithinPolygon)
                && _hits.Count > 0)
            {
                pose = _hits[0].pose;
                return true;
            }

            // 多边形没打中就打平面的包围盒：边缘上那一圈也能放，比"点了没反应"友好
            _hits.Clear();
            if (_raycast.Raycast(screenPosition, _hits, TrackableType.PlaneWithinBounds)
                && _hits.Count > 0)
            {
                pose = _hits[0].pose;
                return true;
            }

            pose = default(Pose);
            return false;
        }

        void FaceCamera()
        {
            Vector3 forward = _camera.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                return;
            }

            _yaw = Quaternion.LookRotation(-forward.normalized, Vector3.up).eulerAngles.y;
            ApplyRotation();
        }

        void ApplyRotation()
        {
            if (_target == null)
            {
                return;
            }

            _target.rotation = Quaternion.Euler(0f, _yaw, 0f) * _tilt;
        }

        void ShowIndicator(bool visible, Pose pose)
        {
            if (_indicator == null)
            {
                return;
            }

            if (_indicator.activeSelf != visible)
            {
                _indicator.SetActive(visible);
            }

            if (visible && _camera != null)
            {
                _indicator.transform.SetPositionAndRotation(pose.position,
                    Quaternion.Euler(0f, _camera.transform.eulerAngles.y, 0f));
            }
        }

        /// <summary>没接落点提示就现做一个半透明圆盘（不生成就没法知道会落在哪）</summary>
        GameObject CreateIndicator()
        {
            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "AR Placement Indicator";

            Collider collider = disc.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            disc.transform.localScale = new Vector3(_indicatorDiameter, 0.004f, _indicatorDiameter);

            Renderer renderer = disc.GetComponent<Renderer>();
            Shader shader = Shader.Find("Sprites/Default");
            if (renderer != null && shader != null)
            {
                Material material = new Material(shader);
                material.color = new Color(1f, 0.95f, 0.6f, 0.5f);
                renderer.sharedMaterial = material;
            }

            disc.SetActive(false);
            return disc;
        }
    }
}
