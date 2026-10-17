using System.Collections.Generic;
using MaidHome.Core.Settings;
using UnityEngine;
using UnityEngine.Rendering;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 真实流泪：照 moreanimation 的 LocalTears / TearProfile 做一套轻量版。
    /// 泪痕是两条从眼角沿脸颊往下的软边布带，挂在头骨骼下面跟着动画走；
    /// 没有眼睛骨骼的方块模型用 MaidFaceRig 的正脸 UV 反推眼角；
    /// 泪滴用世界空间粒子，头歪到接近侧躺时不滴。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidTears : MonoBehaviour
    {
        const int PathPoints = 5;
        const float FadeSeconds = 0.3f;
        const float GrowSeconds = 0.35f;
        const string TearTexturePath = "MoreAnimation/tear";
        const string DropTexturePath = "MoreAnimation/tear_drop";

        [SerializeField] private float _streakWidthRatio = 0.12f;
        [SerializeField] private float _dropSizeRatio = 0.25f;
        [SerializeField] private float _dropInterval = 0.5f;
        [Tooltip("贴到头网格表面后再往前推的极小距离，防止 z-fighting")]
        [SerializeField] private float _surfaceOffset = 0.01f;
        [Tooltip("脸部方框在 Head 局部坐标里的边长（模型 1 格 = 16 像素，8 像素头约 0.5）")]
        [SerializeField] private float _faceSize = 0.46f;
        [Tooltip("泪痕所在的正面深度（Head 局部 Z），8 像素头约 0.23")]
        [SerializeField] private float _faceForward = 0.25f;
        [Tooltip("整张泪痕在 Head 局部坐标里的微调（x 右为正，y 上为正）")]
        [SerializeField] private Vector2 _faceOffset = Vector2.zero;
        [Tooltip("泪滴粒子贴图；留空会尝试 Resources/MoreAnimation/tear_drop，都没有就不生成泪滴")]
        [SerializeField] private Texture2D _dropTexture;
        [Tooltip("连续受伤累计多少点触发一次流泪")]
        [SerializeField] private float _damageThreshold = 20f;
        [Tooltip("多久没有新伤就清空累计")]
        [SerializeField] private float _damageWindow = 30f;
        [SerializeField] private float _crySeconds = 20f;
        [Tooltip("扇脸连击触发后，连击结束时泪痕还会保留多久")]
        [SerializeField] private float _slapCrySeconds = 20f;

        static readonly float[] PathT = { 0f, 0.18f, 0.45f, 0.75f, 1f };
        static readonly string[] CryLines =
        {
            "主人……好痛啊……",
            "主人，我受伤了……",
            "呜……真的很疼……",
            "主人……可以让我休息一下吗？",
            "疼疼疼……主人……",
        };

        static Material _streakMaterial;
        static Texture2D _fallbackStreak;
        static bool _dropResourceChecked;
        static Texture2D _dropResource;

        readonly Vector3[] _pathLeft = new Vector3[PathPoints];
        readonly Vector3[] _pathRight = new Vector3[PathPoints];
        readonly List<Vector3> _vertices = new List<Vector3>();
        readonly List<Vector3> _normals = new List<Vector3>();
        readonly List<Vector2> _uvs = new List<Vector2>();
        readonly List<Color> _colors = new List<Color>();
        readonly List<int> _triangles = new List<int>();
        readonly List<Vector3> _headVertices = new List<Vector3>();
        readonly List<int> _headTriangles = new List<int>();

        MaidAgent _agent;
        MaidFaceRig _rig;
        Transform _head;
        MeshFilter _streakFilter;
        MeshRenderer _streakRenderer;
        Mesh _streakMesh;
        ParticleSystem _drops;
        Material _dropMaterial;
        float _width;
        float _dropSize;
        bool _headSurfaceReady;

        bool _visible;
        bool _held;
        float _startedAt = -100f;
        float _until = -100f;
        float _nextDropAt;
        float _damageTotal;
        float _lastDamageAt = -100f;

        public bool IsCrying
        {
            get { return _visible; }
        }

        void Awake()
        {
            _agent = GetComponent<MaidAgent>();
        }

        void OnDisable()
        {
            Deactivate();
        }

        void Update()
        {
            if (!_visible)
            {
                return;
            }

            if (!GameSettingsStore.Current.TearsEnabled)
            {
                Deactivate();
                return;
            }

            if (!MaidInjuryRules.Allows(_agent))
            {
                Deactivate();
                return;
            }

            if (_rig == null || _rig.Head == null || _head != _rig.Head)
            {
                BuildRig();
            }

            if (_rig == null || !_rig.Supports || _head == null)
            {
                Deactivate();
                return;
            }

            float now = Time.unscaledTime;
            if (!_held && now >= _until + FadeSeconds)
            {
                Deactivate();
                return;
            }

            float alpha = 1f;
            if (!_held && now > _until)
            {
                alpha = Mathf.Clamp01(1f - (now - _until) / FadeSeconds);
            }

            alpha *= 0.86f + 0.04f * Mathf.Sin(now * 9f);
            float grow = Mathf.Clamp01((now - _startedAt) / GrowSeconds);
            RebuildMesh(grow, alpha);
            EnsureDrops();

            if (grow >= 1f && now >= _nextDropAt && HeadUpright())
            {
                EmitDrops();
                _nextDropAt = now + Mathf.Max(0.1f, _dropInterval);
            }
        }

        public void PlayCry(float seconds)
        {
            Activate();
            if (!_visible)
            {
                return;
            }

            if (_held)
            {
                return;
            }

            float end = Time.unscaledTime + Mathf.Max(0.1f, seconds);
            if (end > _until)
            {
                _until = end;
            }
        }

        public void HoldCry()
        {
            Activate();
            if (!_visible)
            {
                return;
            }

            _held = true;
            _until = float.PositiveInfinity;
        }

        public void ReleaseCry(float seconds)
        {
            if (!_visible)
            {
                return;
            }

            _held = false;
            _until = Time.unscaledTime + Mathf.Max(0.1f, seconds);
        }

        public void StopCry()
        {
            _held = false;
            _until = Time.unscaledTime;
        }

        public void AddDamage(float amount)
        {
            if (!GameSettingsStore.Current.TearsEnabled || !MaidInjuryRules.Allows(_agent)
                || !(amount > 0f))
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now - _lastDamageAt > Mathf.Max(1f, _damageWindow))
            {
                _damageTotal = 0f;
            }

            _lastDamageAt = now;
            _damageTotal += amount;
            if (_damageTotal < Mathf.Max(1f, _damageThreshold))
            {
                return;
            }

            _damageTotal = 0f;
            PlayCry(_crySeconds);
            ShowCryLine();
        }

        [ContextMenu("哭 10 秒")]
        public void PlayTest()
        {
            PlayCry(10f);
        }

        [ContextMenu("停止流泪")]
        public void StopTest()
        {
            StopCry();
        }

        void ShowCryLine()
        {
            if (_agent == null)
            {
                _agent = GetComponent<MaidAgent>();
            }

            if (_agent != null)
            {
                MaidChatBubble.Show(_agent, CryLines[Random.Range(0, CryLines.Length)], 2.5f);
            }
        }

        void Activate()
        {
            if (!GameSettingsStore.Current.TearsEnabled || !MaidInjuryRules.Allows(_agent))
            {
                return;
            }

            if (_visible)
            {
                if (_streakRenderer != null)
                {
                    _streakRenderer.enabled = true;
                }

                if (_drops != null && !_drops.isPlaying)
                {
                    _drops.Play();
                }

                return;
            }

            if (!EnsureVisuals())
            {
                return;
            }

            _visible = true;
            _startedAt = Time.unscaledTime;
            _until = _startedAt + Mathf.Max(0.1f, _crySeconds);
            _nextDropAt = _startedAt + GrowSeconds + 0.05f;
            _streakRenderer.enabled = true;
            if (_drops != null)
            {
                _drops.Play();
            }
        }

        void Deactivate()
        {
            _visible = false;
            _held = false;
            _until = -100f;
            if (_streakRenderer != null)
            {
                _streakRenderer.enabled = false;
            }

            if (_streakMesh != null)
            {
                _streakMesh.Clear();
            }

            if (_drops != null)
            {
                _drops.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        bool EnsureVisuals()
        {
            BuildRig();
            if (_rig == null || !_rig.Supports || _head == null)
            {
                return false;
            }

            if (_streakFilter == null)
            {
                GameObject go = new GameObject("TearStreaks");
                go.transform.SetParent(_head, false);
                _streakFilter = go.AddComponent<MeshFilter>();
                _streakRenderer = go.AddComponent<MeshRenderer>();
                _streakRenderer.sharedMaterial = StreakMaterial();
                _streakRenderer.shadowCastingMode = ShadowCastingMode.Off;
                _streakRenderer.receiveShadows = false;
                _streakRenderer.sortingOrder = 15;
                _streakMesh = new Mesh();
                _streakMesh.name = "MaidTearStreaks";
                _streakFilter.sharedMesh = _streakMesh;
            }

            EnsureDrops();

            return true;
        }

        void EnsureDrops()
        {
            if (_drops != null)
            {
                return;
            }

            Texture2D texture = ResolveDropTexture();
            if (texture == null)
            {
                return;
            }

            GameObject go = new GameObject("TearDrops");
            go.transform.SetParent(transform, false);
            _drops = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = _drops.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 0.7f;
            main.startSpeed = 0f;
            main.startSize = _dropSize;
            main.startColor = new Color(1f, 1f, 1f, 0.9f);
            main.gravityModifier = 0.05f;
            main.maxParticles = 64;
            ParticleSystem.EmissionModule emission = _drops.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = _drops.shape;
            shape.enabled = false;
            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = DropMaterial(texture);
            renderer.sortingOrder = 16;
            _drops.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        Texture2D ResolveDropTexture()
        {
            if (_dropTexture != null)
            {
                return _dropTexture;
            }

            if (!_dropResourceChecked)
            {
                _dropResourceChecked = true;
                _dropResource = Resources.Load<Texture2D>(DropTexturePath);
            }

            return _dropResource;
        }

        void BuildRig()
        {
            _rig = MaidFaceRig.Build(transform);
            _head = _rig != null ? _rig.Head : null;
            if (_head == null)
            {
                return;
            }

            if (_streakFilter != null && _streakFilter.transform.parent != _head)
            {
                _streakFilter.transform.SetParent(_head, false);
            }

            CacheHeadSurface();
            BuildPaths();
        }

        void CacheHeadSurface()
        {
            _headVertices.Clear();
            _headTriangles.Clear();
            _headSurfaceReady = false;
            if (_head == null)
            {
                return;
            }

            MeshFilter filter = _head.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null)
            {
                return;
            }

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            for (int i = 0; i < vertices.Length; i++)
            {
                _headVertices.Add(_head.InverseTransformPoint(filter.transform.TransformPoint(vertices[i])));
            }

            for (int i = 0; i < triangles.Length; i++)
            {
                _headTriangles.Add(triangles[i]);
            }

            _headSurfaceReady = _headVertices.Count > 0 && _headTriangles.Count >= 3;
        }

        void BuildPaths()
        {
            // 只用 Head 的枢轴和固定脸部尺寸。TLM 的头部枢轴通常在脖子（脸的下沿），
            // 所以方框从 0 往上长；不读取任何子部件（头发、耳朵）的网格或碰撞箱。
            float faceWidth = Mathf.Max(0.05f, _faceSize);
            float faceHeight = faceWidth;
            float minY = -faceHeight * 0.04f;
            float maxY = minY + faceHeight;
            float planeZ = Mathf.Max(0.01f, _faceForward);

            Vector3 left;
            Vector3 right;
            bool hasEyes = TryEyeLocalPoints(out left, out right);
            float centerX = 0f;
            if (hasEyes)
            {
                centerX = (left.x + right.x) * 0.5f;
                // 眼睛骨骼的 Y 对准脸框 28% 的位置，兼容枢轴在头中间的模型
                float expectedEyeY = minY + faceHeight * 0.28f;
                float shift = Mathf.Clamp((left.y + right.y) * 0.5f - expectedEyeY,
                    -faceHeight * 0.4f, faceHeight * 0.4f);
                minY += shift;
                maxY += shift;
            }

            centerX += _faceOffset.x;
            minY += _faceOffset.y;
            maxY += _faceOffset.y;

            float minX = centerX - faceWidth * 0.5f;
            float maxX = centerX + faceWidth * 0.5f;
            if (!hasEyes)
            {
                float eyeY = Mathf.Lerp(minY, maxY, 0.3f);
                float offset = faceWidth * 0.25f;
                left = new Vector3(centerX - offset, eyeY, planeZ);
                right = new Vector3(centerX + offset, eyeY, planeZ);
            }
            else
            {
                // 从外眼角起笔，不要从眼球中心往下流
                float outer = faceWidth * 0.12f;
                if (left.x <= centerX)
                {
                    left.x -= outer;
                }
                else
                {
                    left.x += outer;
                }

                if (right.x <= centerX)
                {
                    right.x -= outer;
                }
                else
                {
                    right.x += outer;
                }

                left.x = Mathf.Clamp(left.x, minX + faceWidth * 0.08f, maxX - faceWidth * 0.08f);
                right.x = Mathf.Clamp(right.x, minX + faceWidth * 0.08f, maxX - faceWidth * 0.08f);
                left.z = planeZ;
                right.z = planeZ;
            }

            _width = Mathf.Max(0.01f, faceWidth * _streakWidthRatio);
            _dropSize = Mathf.Max(0.01f, _width * _dropSizeRatio);
            BuildPath(left, centerX, minY, maxY, planeZ, _pathLeft);
            BuildPath(right, centerX, minY, maxY, planeZ, _pathRight);
        }

        bool TryEyeLocalPoints(out Vector3 left, out Vector3 right)
        {
            if (_rig.LeftEye != null && _rig.RightEye != null)
            {
                left = EyeCenter(_rig.LeftEye);
                right = EyeCenter(_rig.RightEye);
                if (Mathf.Abs(left.x - right.x) > 0.01f)
                {
                    return true;
                }
            }

            left = Vector3.zero;
            right = Vector3.zero;
            return false;
        }

        /// <summary>眼睛网格的中心比骨骼枢轴更接近可见眼球，优先用它。</summary>
        Vector3 EyeCenter(Transform bone)
        {
            Vector3 point = _head.InverseTransformPoint(bone.position);
            Renderer renderer = bone.GetComponent<Renderer>();
            if (renderer == null)
            {
                return point;
            }

            Bounds bounds;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                bounds = filter.sharedMesh.bounds;
            }
            else
            {
                SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
                if (skinned == null)
                {
                    return point;
                }

                bounds = skinned.localBounds;
            }

            return _head.InverseTransformPoint(renderer.transform.TransformPoint(bounds.center));
        }

        void BuildPath(Vector3 eye, float centerX, float minY, float maxY, float planeZ, Vector3[] path)
        {
            float height = maxY - minY;
            float top = Mathf.Clamp(eye.y - height * 0.02f, minY + height * 0.2f, maxY);
            float bottom = minY + height * 0.03f;
            if (top <= bottom + height * 0.08f)
            {
                top = Mathf.Lerp(minY, maxY, 0.5f);
            }

            float startX = Mathf.Lerp(eye.x, centerX, 0.06f);
            float fallbackZ = Mathf.Max(planeZ, eye.z);
            for (int i = 0; i < PathPoints; i++)
            {
                float t = PathT[i];
                float x = Mathf.Lerp(startX, centerX, 0.1f * t * t);
                float y = Mathf.Lerp(top, bottom, t);
                float z;
                if (!TryHeadSurfaceZ(x, y, out z))
                {
                    z = fallbackZ;
                }

                path[i] = new Vector3(x, y, z + _surfaceOffset);
            }
        }

        /// <summary>
        /// 在 Head 自己的网格上找 (x,y) 处最近的正面表面。
        /// 同一位置有多层（脸 + 外层头发）时取离头中心最近的一层。
        /// </summary>
        bool TryHeadSurfaceZ(float x, float y, out float z)
        {
            z = 0f;
            if (!_headSurfaceReady)
            {
                return false;
            }

            float best = float.PositiveInfinity;
            for (int i = 0; i + 2 < _headTriangles.Count; i += 3)
            {
                Vector3 a = _headVertices[_headTriangles[i]];
                Vector3 b = _headVertices[_headTriangles[i + 1]];
                Vector3 c = _headVertices[_headTriangles[i + 2]];
                Vector3 cross = Vector3.Cross(b - a, c - a);
                if (cross.z <= 0.0000001f)
                {
                    continue;
                }

                float det = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
                if (Mathf.Abs(det) < 0.0000001f)
                {
                    continue;
                }

                float u = ((b.y - c.y) * (x - c.x) + (c.x - b.x) * (y - c.y)) / det;
                float v = ((c.y - a.y) * (x - c.x) + (a.x - c.x) * (y - c.y)) / det;
                float w = 1f - u - v;
                if (u < -0.001f || v < -0.001f || w < -0.001f)
                {
                    continue;
                }

                float surface = u * a.z + v * b.z + w * c.z;
                if (surface < best)
                {
                    best = surface;
                }
            }

            if (float.IsPositiveInfinity(best))
            {
                return false;
            }

            z = best;
            return true;
        }

        void RebuildMesh(float grow, float alpha)
        {
            if (_streakMesh == null)
            {
                return;
            }

            _vertices.Clear();
            _normals.Clear();
            _uvs.Clear();
            _colors.Clear();
            _triangles.Clear();
            AppendPath(_pathLeft, grow, alpha);
            AppendPath(_pathRight, grow, alpha);

            _streakMesh.Clear();
            _streakMesh.SetVertices(_vertices);
            _streakMesh.SetNormals(_normals);
            _streakMesh.SetUVs(0, _uvs);
            _streakMesh.SetColors(_colors);
            _streakMesh.SetTriangles(_triangles, 0);
            _streakMesh.RecalculateBounds();
        }

        void AppendPath(Vector3[] path, float grow, float alpha)
        {
            if (path == null || grow <= 0f)
            {
                return;
            }

            float total = 0f;
            for (int i = 1; i < PathPoints; i++)
            {
                total += Vector3.Distance(path[i - 1], path[i]);
            }

            if (total <= 0.00001f)
            {
                return;
            }

            float target = total * Mathf.Clamp01(grow);
            float traveled = 0f;
            Vector3 half = Vector3.right * (_width * 0.5f);
            for (int i = 1; i < PathPoints; i++)
            {
                if (traveled >= target)
                {
                    break;
                }

                Vector3 a = path[i - 1];
                Vector3 b = path[i];
                float segment = Vector3.Distance(a, b);
                if (segment <= 0.00001f)
                {
                    continue;
                }

                float t = Mathf.Clamp01((target - traveled) / segment);
                Vector3 end = Vector3.Lerp(a, b, t);
                AddQuad(a, end, half, alpha, traveled / total, (traveled + segment * t) / total);
                traveled += segment;
            }
        }

        void AddQuad(Vector3 a, Vector3 b, Vector3 half, float alpha, float v0, float v1)
        {
            int index = _vertices.Count;
            Vector3 leftA = a - half;
            Vector3 rightA = a + half;
            Vector3 rightB = b + half;
            Vector3 leftB = b - half;
            _vertices.Add(leftA);
            _vertices.Add(rightA);
            _vertices.Add(rightB);
            _vertices.Add(leftB);
            _normals.Add(Vector3.forward);
            _normals.Add(Vector3.forward);
            _normals.Add(Vector3.forward);
            _normals.Add(Vector3.forward);
            _uvs.Add(new Vector2(0f, v0));
            _uvs.Add(new Vector2(1f, v0));
            _uvs.Add(new Vector2(1f, v1));
            _uvs.Add(new Vector2(0f, v1));
            Color color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            _colors.Add(color);
            _colors.Add(color);
            _colors.Add(color);
            _colors.Add(color);
            _triangles.Add(index);
            _triangles.Add(index + 2);
            _triangles.Add(index + 1);
            _triangles.Add(index);
            _triangles.Add(index + 3);
            _triangles.Add(index + 2);
        }

        bool HeadUpright()
        {
            return _head != null && Vector3.Dot(_head.up, Vector3.up) > 0.5f;
        }

        void EmitDrops()
        {
            if (_drops == null || _head == null)
            {
                return;
            }

            EmitDrop(_head.TransformPoint(_pathLeft[PathPoints - 1]));
            EmitDrop(_head.TransformPoint(_pathRight[PathPoints - 1]));
        }

        void EmitDrop(Vector3 position)
        {
            ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams();
            emit.position = position;
            emit.velocity = Vector3.down * 0.06f;
            emit.startLifetime = 0.7f;
            emit.startSize = _dropSize;
            emit.startColor = new Color(1f, 1f, 1f, 0.92f);
            _drops.Emit(emit, 1);
        }

        static Material StreakMaterial()
        {
            if (_streakMaterial != null)
            {
                return _streakMaterial;
            }

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Transparent");
            }

            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            _streakMaterial = new Material(shader);
            _streakMaterial.name = "MaidTearStreak";
            Texture2D texture = Resources.Load<Texture2D>(TearTexturePath);
            _streakMaterial.mainTexture = texture != null ? texture : FallbackStreakTexture();
            _streakMaterial.renderQueue = 3000;
            return _streakMaterial;
        }

        Material DropMaterial(Texture2D texture)
        {
            if (_dropMaterial != null)
            {
                return _dropMaterial;
            }

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            }

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Transparent");
            }

            _dropMaterial = new Material(shader);
            _dropMaterial.name = "MaidTearDrop";
            _dropMaterial.mainTexture = texture;
            _dropMaterial.renderQueue = 3000;
            return _dropMaterial;
        }

        static Texture2D FallbackStreakTexture()
        {
            if (_fallbackStreak != null)
            {
                return _fallbackStreak;
            }

            const int width = 16;
            const int height = 4;
            _fallbackStreak = new Texture2D(width, height, TextureFormat.RGBA32, false);
            _fallbackStreak.name = "MaidTearFallback";
            _fallbackStreak.wrapMode = TextureWrapMode.Clamp;
            _fallbackStreak.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float distance = Mathf.Abs((x + 0.5f) / width * 2f - 1f);
                    float alpha = Mathf.Clamp01(1f - distance * 1.4f);
                    pixels[y * width + x] = new Color(0.18f, 0.53f, 0.92f, alpha);
                }
            }

            _fallbackStreak.SetPixels(pixels);
            _fallbackStreak.Apply();
            return _fallbackStreak;
        }

        public static MaidTears Find(MaidAgent agent)
        {
            return agent != null ? agent.GetComponent<MaidTears>() : null;
        }

        public static MaidTears Ensure(MaidAgent agent)
        {
            if (agent == null)
            {
                return null;
            }

            MaidTears tears = agent.GetComponent<MaidTears>();
            if (tears == null)
            {
                tears = agent.gameObject.AddComponent<MaidTears>();
            }

            return tears;
        }

        /// <summary>累计受伤；同一次战斗里超过阈值就哭一场</summary>
        public static void Hurt(MaidAgent agent, float amount)
        {
            MaidTears tears = Ensure(agent);
            if (tears != null)
            {
                tears.AddDamage(amount);
            }
        }

        /// <summary>扇脸连击到十下：一直流泪，直到连击断掉</summary>
        public static void HoldSlapCry(MaidAgent agent)
        {
            MaidTears tears = Ensure(agent);
            if (tears != null)
            {
                tears.HoldCry();
            }
        }

        /// <summary>连击结束：泪痕再保留一段，然后淡出</summary>
        public static void FinishSlapCry(MaidAgent agent)
        {
            MaidTears tears = Find(agent);
            if (tears != null)
            {
                tears.ReleaseCry(tears._slapCrySeconds);
            }
        }
    }
}
