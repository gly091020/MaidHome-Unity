using System;
using System.Collections;
using UnityEngine;

namespace MaidHome.Gameplay.House
{
    /// <summary>
    /// 相机取景：房子加载后按包围盒自动取景；点女仆时平滑拉近，取消后回到房子视角。
    /// 正交相机拉远不会改变画面大小，所以 Focus/House 两个视角都用“切 size + 平移”实现。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class HouseCameraFitter : MonoBehaviour
    {
        public static HouseCameraFitter Instance { get; private set; }

        [SerializeField] private Camera _camera;
        [Tooltip("房子取景时包围盒外额外留白的倍率，1.15 = 多留 15%")]
        [SerializeField] private float _margin = 1.15f;
        [SerializeField] private float _minOrthographicSize = 4f;
        [SerializeField] private float _maxOrthographicSize = 200f;
        [Tooltip("正交相机额外后退的距离，后退不会改变画面大小，但能避开近裁剪面")]
        [SerializeField] private float _extraDistance = 30f;
        [Tooltip("进 Play 后如果房子已经加载了，自动取一次景")]
        [SerializeField] private bool _fitOnStart = true;

        [Header("交互取景")]
        [Tooltip("点女仆后画面的留白倍率")]
        [SerializeField] private float _focusMargin = 1.6f;
        [SerializeField] private float _focusExtraDistance = 2f;
        [SerializeField] private float _focusWallPadding = 0.25f;
        [Tooltip("看向女仆时，取景中心往上抬多少（按包围盒半高比例）")]
        [SerializeField] private float _focusLookHeightRatio = 0.45f;
        [Tooltip("聚焦动画走到一半时切成透视相机")]
        [SerializeField] private bool _switchToPerspectiveAtHalf = true;
        [Tooltip("取消交互时切回正交相机")]
        [SerializeField] private bool _switchBackToOrthographicOnCancel = true;
        [SerializeField] private float _focusSeconds = 0.35f;

        struct CameraView
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public float OrthographicSize;
            public float FieldOfView;
            public float FarClip;
            public bool Orthographic;
        }

        Coroutine _moveRoutine;
        CameraView _houseView;
        bool _hasHouseView;

        void Awake()
        {
            Instance = this;
            if (_camera == null)
            {
                _camera = GetComponent<Camera>();
            }
        }

        void Start()
        {
            if (_fitOnStart)
            {
                FitCurrentHouse();
            }
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void FitCurrentHouse()
        {
            if (HouseContext.View == null)
            {
                return;
            }

            FitTo(HouseContext.View.gameObject);
        }

        public bool FitTo(GameObject houseRoot)
        {
            if (_camera == null || houseRoot == null)
            {
                return false;
            }

            Bounds bounds;
            if (!TryGetBounds(houseRoot, out bounds))
            {
                return false;
            }

            FitToBounds(bounds);
            return true;
        }

        public void FitToBounds(Bounds bounds)
        {
            if (_camera == null)
            {
                return;
            }

            StopMove();
            CameraView view = CalculateView(bounds, _margin, _extraDistance, _minOrthographicSize,
                _maxOrthographicSize);
            ApplyView(view);
            _houseView = view;
            _hasHouseView = true;
        }

        public void FocusOn(Bounds bounds)
        {
            FocusOn(bounds, null);
        }

        public void FocusOn(Bounds bounds, Transform subject)
        {
            if (_camera == null)
            {
                return;
            }

            if (!_hasHouseView)
            {
                _houseView = CaptureView();
                _hasHouseView = true;
            }

            StopMove();
            CameraView view = subject != null
                ? CalculateFocusView(bounds, subject, _focusMargin, _focusExtraDistance, 1.5f, _maxOrthographicSize)
                : CalculateView(bounds, _focusMargin, _focusExtraDistance, 1.5f, _maxOrthographicSize);
            _moveRoutine = StartCoroutine(MoveTo(view, _focusSeconds, _switchToPerspectiveAtHalf, false));
        }

        public void RestoreHouseView()
        {
            if (_camera == null)
            {
                return;
            }

            StopMove();
            if (!_hasHouseView)
            {
                FitCurrentHouse();
                return;
            }

            _moveRoutine = StartCoroutine(MoveTo(_houseView, _focusSeconds, false,
                _switchBackToOrthographicOnCancel));
        }

        /// <summary>
        /// 站到 anchor 外侧的某个位置（吸尾巴用）。direction 是"从 anchor 指向相机"的水平方向，
        /// height 是相机比 anchor 高多少；路上有墙就停在墙前。
        /// subject 是射线遮挡判断里要跳过的对象（女仆自己）。
        /// 看向并框住的是 fit 的包围盒：位置和看的方向拆开，是因为站在尾巴后面看尾巴尖的话，
        /// 女仆的头会偏出画面很远，反推出来的视野角能到 90 度以上，变成鱼眼。
        /// FOV 也按新机位重算——不能沿用当前视野角，那个角是按远处框住整只女仆算的。
        /// </summary>
        public void FocusOnPoint(Vector3 anchor, Vector3 direction, float distance, float height,
            Transform subject, Bounds fit, float fitMargin)
        {
            if (_camera == null)
            {
                return;
            }

            if (!_hasHouseView)
            {
                _houseView = CaptureView();
                _hasHouseView = true;
            }

            StopMove();

            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = Vector3.forward;
            }

            direction.Normalize();
            Vector3 offset = direction * Mathf.Max(0.2f, distance) + Vector3.up * Mathf.Max(0f, height);
            Vector3 offsetDirection = offset.normalized;
            float safe = ResolveFocusDistance(anchor, offsetDirection, offset.magnitude, subject);
            Vector3 position = anchor + offsetDirection * safe;
            Vector3 lookAt = fit.center;
            Vector3 lookDirection = lookAt - position;
            if (lookDirection.sqrMagnitude < 0.0001f)
            {
                lookDirection = -offsetDirection;
            }

            CameraView view = new CameraView();
            view.Orthographic = false;
            view.Position = position;
            view.Rotation = Quaternion.LookRotation(lookDirection, Vector3.up);
            view.OrthographicSize = _camera.orthographicSize;
            view.FarClip = Mathf.Max(_camera.farClipPlane, safe + 20f);
            view.FieldOfView = CalculateFieldOfView(fit, view.Position, view.Rotation, fitMargin);

            // 从正面绕到背面时直线插值会从女仆身上穿过去，所以抬一个中间控制点走弧线
            Vector3 start = _camera.transform.position;
            Vector3 via = Vector3.Lerp(start, position, 0.5f)
                + Vector3.up * (Vector3.Distance(start, position) * 0.3f);
            _moveRoutine = StartCoroutine(MoveTo(view, _focusSeconds, false, false, true, via));
        }

        /// 按包围盒在新机位下占多大，反推需要的视野角
        float CalculateFieldOfView(Bounds bounds, Vector3 position, Quaternion rotation, float margin)
        {
            Vector3 forward = rotation * Vector3.forward;
            float centerDistance = Mathf.Max(0.05f, Vector3.Dot(bounds.center - position, forward));
            float halfWidth;
            float halfHeight;
            MeasureInView(bounds, position, rotation, out halfWidth, out halfHeight);
            margin = Mathf.Max(0.1f, margin);
            halfWidth *= margin;
            halfHeight *= margin;

            float aspect = _camera != null && _camera.aspect > 0.001f ? _camera.aspect : 16f / 9f;
            float requiredVertical = Mathf.Atan2(halfHeight, centerDistance) * Mathf.Rad2Deg * 2f;
            float requiredHorizontal = Mathf.Atan2(halfWidth, centerDistance) * Mathf.Rad2Deg * 2f;
            float horizontalToVertical = Mathf.Atan(
                Mathf.Tan(requiredHorizontal * Mathf.Deg2Rad * 0.5f) / aspect) * Mathf.Rad2Deg * 2f;
            return Mathf.Clamp(Mathf.Max(requiredVertical, horizontalToVertical), 8f, 80f);
        }

        [ContextMenu("Fit To Current House")]
        void FitFromContextMenu()
        {
            FitCurrentHouse();
        }

        CameraView CalculateView(Bounds bounds, float margin, float extraDistance, float minSize, float maxSize)
        {
            CameraView view = new CameraView();
            view.Orthographic = true;
            Transform cameraTransform = _camera.transform;
            view.Rotation = cameraTransform.rotation;

            Vector3 forward = view.Rotation * Vector3.forward;
            float distance = Vector3.Dot(cameraTransform.position - bounds.center, forward);
            float safeDistance = bounds.extents.magnitude * 2f + _camera.nearClipPlane + 1f
                + Mathf.Max(0f, extraDistance);
            if (distance < safeDistance)
            {
                distance = safeDistance;
            }

            view.Position = bounds.center - forward * distance;
            view.FarClip = Mathf.Max(_camera.farClipPlane, distance + bounds.extents.magnitude + 10f);

            float halfWidth;
            float halfHeight;
            MeasureInView(bounds, view.Position, view.Rotation, out halfWidth, out halfHeight);
            halfWidth *= margin;
            halfHeight *= margin;

            float aspect = _camera.aspect > 0.001f ? _camera.aspect : 16f / 9f;
            view.OrthographicSize = Mathf.Clamp(Mathf.Max(halfHeight, halfWidth / aspect), minSize, maxSize);

            float requiredVertical = Mathf.Atan2(halfHeight, distance) * Mathf.Rad2Deg * 2f;
            float requiredHorizontal = Mathf.Atan2(halfWidth, distance) * Mathf.Rad2Deg * 2f;
            float horizontalToVertical = Mathf.Atan(Mathf.Tan(requiredHorizontal * Mathf.Deg2Rad * 0.5f)
                / aspect) * Mathf.Rad2Deg * 2f;
            view.FieldOfView = Mathf.Max(requiredVertical, horizontalToVertical);
            return view;
        }

        CameraView CalculateFocusView(Bounds bounds, Transform subject, float margin, float extraDistance,
            float minSize, float maxSize)
        {
            Vector3 forward = subject.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.000001f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();
            Vector3 target = bounds.center + Vector3.up * (bounds.extents.y * _focusLookHeightRatio);
            float distance = bounds.extents.magnitude * 2f + _camera.nearClipPlane + 1f
                + Mathf.Max(0f, extraDistance);
            distance = ResolveFocusDistance(target, forward, distance, subject);
            Vector3 position = target + forward * distance;

            CameraView view = new CameraView();
            view.Orthographic = false;
            view.Position = position;
            view.Rotation = Quaternion.LookRotation(target - position, Vector3.up);
            view.FarClip = Mathf.Max(_camera.farClipPlane, distance + bounds.extents.magnitude + 10f);

            float halfWidth;
            float halfHeight;
            MeasureInView(bounds, view.Position, view.Rotation, out halfWidth, out halfHeight);
            halfWidth *= margin;
            halfHeight *= margin;

            float aspect = _camera.aspect > 0.001f ? _camera.aspect : 16f / 9f;
            view.OrthographicSize = Mathf.Clamp(Mathf.Max(halfHeight, halfWidth / aspect), minSize, maxSize);

            float requiredVertical = Mathf.Atan2(halfHeight, distance) * Mathf.Rad2Deg * 2f;
            float requiredHorizontal = Mathf.Atan2(halfWidth, distance) * Mathf.Rad2Deg * 2f;
            float horizontalToVertical = Mathf.Atan(Mathf.Tan(requiredHorizontal * Mathf.Deg2Rad * 0.5f)
                / aspect) * Mathf.Rad2Deg * 2f;
            view.FieldOfView = Mathf.Max(requiredVertical, horizontalToVertical);
            return view;
        }

        float ResolveFocusDistance(Vector3 target, Vector3 forward, float distance, Transform subject)
        {
            RaycastHit[] hits = Physics.RaycastAll(target, forward, distance, ~0);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                Collider collider = hits[i].collider;
                if (collider == null)
                {
                    continue;
                }

                // 射线从女仆头部出发，先跳过她自己
                if (subject != null && (collider.transform == subject || collider.transform.IsChildOf(subject)))
                {
                    continue;
                }

                return Mathf.Max(_camera.nearClipPlane + 0.2f, hits[i].distance - _focusWallPadding);
            }

            return distance;
        }

        void ApplyView(CameraView view)
        {
            Transform cameraTransform = _camera.transform;
            cameraTransform.position = view.Position;
            cameraTransform.rotation = view.Rotation;
            _camera.orthographic = view.Orthographic;
            if (view.Orthographic)
            {
                _camera.orthographicSize = view.OrthographicSize;
            }
            else
            {
                _camera.fieldOfView = view.FieldOfView;
            }

            _camera.farClipPlane = view.FarClip;
        }

        CameraView CaptureView()
        {
            CameraView view = new CameraView();
            view.Position = _camera.transform.position;
            view.Rotation = _camera.transform.rotation;
            view.OrthographicSize = _camera.orthographicSize;
            view.FieldOfView = _camera.fieldOfView;
            view.FarClip = _camera.farClipPlane;
            view.Orthographic = _camera.orthographic;
            return view;
        }

        IEnumerator MoveTo(CameraView target, float duration, bool perspectiveAtHalf, bool orthographicAtStart,
            bool hasVia = false, Vector3 via = default(Vector3))
        {
            Transform cameraTransform = _camera.transform;
            Vector3 startPosition = cameraTransform.position;
            Quaternion startRotation = cameraTransform.rotation;
            float startSize = _camera.orthographicSize;
            float startFov = _camera.fieldOfView;
            float startFar = _camera.farClipPlane;

            if (orthographicAtStart && !_camera.orthographic)
            {
                float distance = Vector3.Distance(cameraTransform.position, target.Position);
                _camera.orthographicSize = MatchOrthographicSize(startFov, distance);
                _camera.orthographic = true;
                startSize = _camera.orthographicSize;
            }

            bool switched = false;
            float fovStart = startFov;
            float fovStartT = 0f;
            float length = Mathf.Max(0.01f, duration);
            float elapsed = 0f;

            while (elapsed < length)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / length));
                cameraTransform.position = hasVia
                    ? Quadratic(startPosition, via, target.Position, t)
                    : Vector3.Lerp(startPosition, target.Position, t);
                cameraTransform.rotation = Quaternion.Slerp(startRotation, target.Rotation, t);

                // 已经是透视的时候别再切一次，否则会拿过期的 orthographicSize 算 FOV，画面会跳
                if (perspectiveAtHalf && !switched && _camera.orthographic && t >= 0.5f)
                {
                    float distance = Vector3.Distance(cameraTransform.position, target.Position);
                    _camera.fieldOfView = MatchFieldOfView(_camera.orthographicSize, distance);
                    _camera.orthographic = false;
                    fovStart = _camera.fieldOfView;
                    fovStartT = t;
                    switched = true;
                }

                if (_camera.orthographic)
                {
                    _camera.orthographicSize = Mathf.Lerp(startSize, target.OrthographicSize, t);
                }
                else
                {
                    float fovT = switched ? Mathf.InverseLerp(fovStartT, 1f, t) : t;
                    _camera.fieldOfView = Mathf.Lerp(fovStart, target.FieldOfView, Mathf.Clamp01(fovT));
                }

                _camera.farClipPlane = Mathf.Lerp(startFar, target.FarClip, t);
                yield return null;
            }

            ApplyView(target);
            _moveRoutine = null;
        }

        /// 二次贝塞尔，用来把镜头走成一条弧
        static Vector3 Quadratic(Vector3 from, Vector3 control, Vector3 to, float t)
        {
            float inverse = 1f - t;
            return inverse * inverse * from + 2f * inverse * t * control + t * t * to;
        }

        static float MatchFieldOfView(float orthographicSize, float distance)
        {
            distance = Mathf.Max(0.01f, distance);
            return 2f * Mathf.Atan2(orthographicSize, distance) * Mathf.Rad2Deg;
        }

        static float MatchOrthographicSize(float fieldOfView, float distance)
        {
            distance = Mathf.Max(0.01f, distance);
            return Mathf.Tan(fieldOfView * Mathf.Deg2Rad * 0.5f) * distance;
        }

        void StopMove()
        {
            if (_moveRoutine != null)
            {
                StopCoroutine(_moveRoutine);
                _moveRoutine = null;
            }
        }

        static void MeasureInView(Bounds bounds, Vector3 position, Quaternion rotation, out float halfWidth,
            out float halfHeight)
        {
            halfWidth = 0f;
            halfHeight = 0f;
            Matrix4x4 worldToView = Matrix4x4.TRS(position, rotation, Vector3.one).inverse;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                    (i & 4) == 0 ? bounds.min.z : bounds.max.z);
                Vector3 local = worldToView.MultiplyPoint3x4(corner);
                halfWidth = Mathf.Max(halfWidth, Mathf.Abs(local.x));
                halfHeight = Mathf.Max(halfHeight, Mathf.Abs(local.y));
            }
        }

        static bool TryGetBounds(GameObject root, out Bounds bounds)
        {
            HouseNavMesh nav = root.GetComponent<HouseNavMesh>();
            if (nav != null && nav.Bounds.size.sqrMagnitude > 0.0001f)
            {
                bounds = nav.Bounds;
                return true;
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                bounds = new Bounds(root.transform.position, Vector3.one);
                return false;
            }

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return true;
        }
    }
}
