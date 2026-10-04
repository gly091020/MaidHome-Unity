using System;
using System.Collections;
using UnityEngine;

namespace MaidHome.Gameplay.House
{
    /// <summary>
    /// 相机取景：房子加载后按包围盒自动取景；点女仆时沿当前方位角推近，取消后回到房子视角。
    /// **全程正交**，不做透视切换：正交相机拉远拉近都不改变透视关系，
    /// 视野大小只用“切 size + 平移”调，远近看着一样也就没有透视畸变。
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

        [Header("拖动 / 缩放（叠加在房子取景上）")]
        [Tooltip("缩放下限：相对于房子取景尺寸的倍率")]
        [SerializeField] private float _zoomMin = 0.3f;
        [Tooltip("缩放上限")]
        [SerializeField] private float _zoomMax = 2.5f;
        [Tooltip("最多能拖出房子多远，按房子包围盒半对角线算")]
        [SerializeField] private float _panLimit = 0.9f;

        [Header("交互取景")]
        [Tooltip("点女仆后画面的留白倍率")]
        [SerializeField] private float _focusMargin = 1.6f;
        [SerializeField] private float _focusExtraDistance = 2f;
        [SerializeField] private float _focusWallPadding = 0.25f;
        [Tooltip("看向女仆时，取景中心往上抬多少（按包围盒半高比例）")]
        [SerializeField] private float _focusLookHeightRatio = 0.45f;
        [Tooltip("交互取景时相机低头多少度，45 = 斜上方俯视（正交下俯角不影响画面大小，只影响看到的侧面）")]
        [SerializeField] private float _focusPitchDegrees = 45f;
        [SerializeField] private float _focusSeconds = 0.35f;

        struct CameraView
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public float OrthographicSize;
            public float FarClip;
        }

        Coroutine _moveRoutine;
        CameraView _houseView;
        Bounds _houseBounds;
        bool _hasHouseView;
        Vector2 _pan;
        float _zoom = 1f;
        float _lift;
        float _appliedLift;

        /// <summary>现在能不能拖动/缩放：有房子取景、而且自己没有正在动画（动画期间让位，别两边抢）</summary>
        public bool CanPanZoom
        {
            get { return _hasHouseView && _moveRoutine == null; }
        }

        /// <summary>取景动画还在跑（跑的期间每次 ApplyView 都会把相机按回正交）</summary>
        public bool IsMoving
        {
            get { return _moveRoutine != null; }
        }

        /// <summary>
        /// 临时切成透视（喂蛋糕的中/重挡要用），传 &lt;= 0 切回正交。
        /// 注意：这套取景只会算正交 size，而且每次 ApplyView 都会把相机设回正交，
        /// 所以要在取景动画停下来之后再切（不然会被它一路按回去），切透视期间也别再重新取景。
        /// </summary>
        public void SetPerspective(float fieldOfView)
        {
            if (_camera == null)
            {
                return;
            }

            if (fieldOfView <= 0f)
            {
                _camera.orthographic = true;
                return;
            }

            _camera.orthographic = false;
            _camera.fieldOfView = fieldOfView;
        }

        /// <summary>
        /// 整个相机在世界上额外抬高多少（切场景过场用：先升到天上、切完再降下来）。
        /// 它是加在**每次 ApplyView** 上的，所以过场期间房子/女仆重新取景不会把相机拽回地面。
        /// 场景里本来摆的机位也会被一起抬（还没取过景时直接平移 transform）。
        /// </summary>
        public void SetLift(float height)
        {
            if (_camera == null)
            {
                return;
            }

            _lift = height;
            if (_hasHouseView)
            {
                ApplyHouseView();
                return;
            }

            _camera.transform.position += Vector3.up * (_lift - _appliedLift);
            _appliedLift = _lift;
        }

        /// <summary>现在抬了多少（过场组件要拿它对齐新旧场景）</summary>
        public float Lift
        {
            get { return _lift; }
        }

        void Awake()
        {
            Instance = this;
            if (_camera == null)
            {
                _camera = GetComponent<Camera>();
            }

            // 这套取景只按正交算 size，相机要是被设成透视，画面大小就不是我们算的那个了
            if (_camera != null)
            {
                _camera.orthographic = true;
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
            _houseBounds = bounds;
            _hasHouseView = true;
            // 换房子/重新取景 = 重新开始，玩家之前的拖动缩放不带到新房子上
            ResetPanZoom();
        }

        /// <summary>
        /// 保持相机现在的方位角推近到框住 bounds，不绕到对象正面。
        /// 女仆交互用这个：按她的朝向反推机位会让整个画面旋转，看起来像女仆自己在转。
        /// 俯角固定成 _focusPitchDegrees（低头看她），正交下远近不改变画面大小，所以退多远都行。
        /// subject 只用来跳过对象自己的碰撞体（射线从她胸口往相机方向打，别打到自己）。
        /// </summary>
        public void FocusKeepingAngle(Bounds bounds, Transform subject)
        {
            FocusKeepingAngle(bounds, subject, 1.5f);
        }

        /// <summary>
        /// 同上，但可以指定最近的取景尺寸。摸脸模式要贴到脸上，1.5 那个下限太大。
        /// </summary>
        public void FocusKeepingAngle(Bounds bounds, Transform subject, float minOrthographicSize)
        {
            FocusKeepingAngle(bounds, subject, minOrthographicSize, _focusPitchDegrees, _focusLookHeightRatio);
        }

        /// <summary>
        /// 同上，但俯角和"取景中心抬多高"由调用方指定。摸脸模式要平视她的脸，
        /// 和平时点开女仆那个 45° 俯视不一样，所以单独传。
        /// </summary>
        public void FocusKeepingAngle(Bounds bounds, Transform subject, float minOrthographicSize,
            float pitchDegrees, float lookHeightRatio)
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
            CameraView view = CalculateKeepingAngleView(bounds, subject, _focusMargin, _focusExtraDistance,
                Mathf.Max(0.05f, minOrthographicSize), _maxOrthographicSize, pitchDegrees, lookHeightRatio);
            _moveRoutine = StartCoroutine(MoveTo(view, _focusSeconds));
        }

        /// <summary>平时点开女仆的俯角，摸脸模式选"沿用默认"时会读这个值</summary>
        public float FocusPitchDegrees
        {
            get { return _focusPitchDegrees; }
        }

        /// <summary>平时点开女仆的取景中心抬高比例</summary>
        public float FocusLookHeightRatio
        {
            get { return _focusLookHeightRatio; }
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

            // 回到房子视角时带上网玩家的拖动/缩放（点开女仆再关掉，视角不该被重置）
            _moveRoutine = StartCoroutine(MoveTo(HouseViewWithOffsets(), _focusSeconds));
        }

        /// <summary>当前正交尺寸下，1 像素等于多少世界单位（正交相机横竖一致）</summary>
        float WorldPerPixel
        {
            get { return 2f * _camera.orthographicSize / Mathf.Max(1f, Screen.height); }
        }

        /// <summary>手指拖动：screenDelta 是这一帧的像素位移，画面跟着手指走（等于相机往反方向挪）</summary>
        public void PanByPixels(Vector2 screenDelta)
        {
            if (!CanPanZoom || screenDelta.sqrMagnitude <= 0f)
            {
                return;
            }

            _pan -= screenDelta * WorldPerPixel;
            ClampPan();
            ApplyHouseView();
        }

        /// <summary>
        /// 以屏幕上的某点为锚点缩放（捏合的中点 / 鼠标位置）：锚点底下的东西在画面上不动。
        /// 正交相机的推导很直接——锚点离屏幕中心 d 像素，缩放 k 倍后它相对相机的位置变化是 d*(1-k) 个世界单位。
        /// </summary>
        public void ZoomBy(float factor, Vector2 screenPoint)
        {
            if (!CanPanZoom || factor <= 0.0001f)
            {
                return;
            }

            float next = Mathf.Clamp(_zoom * factor, _zoomMin, _zoomMax);
            float applied = next / Mathf.Max(0.0001f, _zoom);
            if (Mathf.Abs(applied - 1f) < 0.0001f)
            {
                return;
            }

            Vector2 offset = screenPoint - new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            _pan += offset * WorldPerPixel * (1f - applied);
            _zoom = next;
            ClampPan();
            ApplyHouseView();
        }

        /// <summary>回到房子的原始取景（拖动/缩放清零）</summary>
        [ContextMenu("Reset Pan Zoom")]
        public void ResetPanZoom()
        {
            _pan = Vector2.zero;
            _zoom = 1f;
            if (_hasHouseView)
            {
                ApplyHouseView();
            }
        }

        /// <summary>房子取景 + 玩家的拖动/缩放，离屏动画（回房子视角）也用这个当目标</summary>
        CameraView HouseViewWithOffsets()
        {
            CameraView view = _houseView;
            view.OrthographicSize = Mathf.Clamp(_houseView.OrthographicSize * _zoom, _minOrthographicSize,
                _maxOrthographicSize);
            Vector3 right = _houseView.Rotation * Vector3.right;
            Vector3 up = _houseView.Rotation * Vector3.up;
            view.Position = _houseView.Position + right * _pan.x + up * _pan.y;
            return view;
        }

        void ApplyHouseView()
        {
            if (_camera != null && _hasHouseView)
            {
                ApplyView(HouseViewWithOffsets());
            }
        }

        /// <summary>别让玩家把镜头拖到看不见房子：限制在以房子中心为心的一个圈里</summary>
        void ClampPan()
        {
            float radius = Mathf.Max(1f, _houseBounds.extents.magnitude) * Mathf.Max(0f, _panLimit);
            _pan = Vector2.ClampMagnitude(_pan, radius);
        }

        [ContextMenu("Fit To Current House")]
        void FitFromContextMenu()
        {
            FitCurrentHouse();
        }

        CameraView CalculateView(Bounds bounds, float margin, float extraDistance, float minSize, float maxSize)
        {
            CameraView view = new CameraView();
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
            return view;
        }

        /// 机位方向：方位角取相机自己的（画面不会转），俯角固定低头 _focusPitchDegrees
        CameraView CalculateKeepingAngleView(Bounds bounds, Transform subject, float margin, float extraDistance,
            float minSize, float maxSize, float pitchDegrees, float lookHeightRatio)
        {
            CameraView view = new CameraView();

            // 从女仆指向相机的方向 = 相机视线的反向，先取水平方位角
            Vector3 azimuth = -(_camera.transform.rotation * Vector3.forward);
            azimuth.y = 0f;
            if (azimuth.sqrMagnitude < 0.000001f)
            {
                azimuth = Vector3.back;
            }

            azimuth.Normalize();

            // 再按俯角抬起来：相机站到她的斜上方，低头看她
            float pitch = Mathf.Clamp(pitchDegrees, 0f, 85f) * Mathf.Deg2Rad;
            Vector3 toCamera = azimuth * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);

            Vector3 target = bounds.center + Vector3.up * (bounds.extents.y * lookHeightRatio);
            float distance = bounds.extents.magnitude * 2f + _camera.nearClipPlane + 1f
                + Mathf.Max(0f, extraDistance);
            distance = ResolveFocusDistance(target, toCamera, distance, subject);
            view.Position = target + toCamera * distance;
            view.Rotation = Quaternion.LookRotation(target - view.Position, Vector3.up);
            view.FarClip = Mathf.Max(_camera.farClipPlane, distance + bounds.extents.magnitude + 10f);

            float halfWidth;
            float halfHeight;
            MeasureInView(bounds, view.Position, view.Rotation, out halfWidth, out halfHeight);
            halfWidth *= margin;
            halfHeight *= margin;

            float aspect = _camera.aspect > 0.001f ? _camera.aspect : 16f / 9f;
            view.OrthographicSize = Mathf.Clamp(Mathf.Max(halfHeight, halfWidth / aspect), minSize, maxSize);
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
            cameraTransform.position = view.Position + Vector3.up * _lift;
            cameraTransform.rotation = view.Rotation;
            _camera.orthographic = true;
            _camera.orthographicSize = view.OrthographicSize;
            _camera.farClipPlane = view.FarClip;
            _appliedLift = _lift;
        }

        CameraView CaptureView()
        {
            CameraView view = new CameraView();
            // 存的是"没抬过"的机位，抬升是每次 ApplyView 另外加的
            view.Position = _camera.transform.position - Vector3.up * _appliedLift;
            view.Rotation = _camera.transform.rotation;
            view.OrthographicSize = _camera.orthographicSize;
            view.FarClip = _camera.farClipPlane;
            return view;
        }

        IEnumerator MoveTo(CameraView target, float duration)
        {
            Transform cameraTransform = _camera.transform;
            Vector3 startPosition = cameraTransform.position - Vector3.up * _appliedLift;
            Quaternion startRotation = cameraTransform.rotation;
            float startSize = _camera.orthographicSize;
            float startFar = _camera.farClipPlane;

            float length = Mathf.Max(0.01f, duration);
            float elapsed = 0f;

            while (elapsed < length)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / length));
                cameraTransform.position = Vector3.Lerp(startPosition, target.Position, t) + Vector3.up * _lift;
                cameraTransform.rotation = Quaternion.Slerp(startRotation, target.Rotation, t);
                _camera.orthographicSize = Mathf.Lerp(startSize, target.OrthographicSize, t);
                _camera.farClipPlane = Mathf.Lerp(startFar, target.FarClip, t);
                _appliedLift = _lift;
                yield return null;
            }

            ApplyView(target);
            _moveRoutine = null;
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
