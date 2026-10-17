using UnityEngine;
using UnityEngine.Rendering;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 按傻后头顶绕圈的星星，照 SableMaidRagdoll 的 renderStars 做的网格版：
    /// 一颗星一个 quad，统一一张 Mesh，绕局部 Y 轴转，面朝中心并带随机滚转。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidHeadStars : MonoBehaviour
    {
        Mesh _mesh;
        MeshFilter _filter;
        MeshRenderer _renderer;
        Vector3[] _vertices;
        Vector2[] _uvs;
        int[] _triangles;
        float[] _radii;
        float[] _heights;
        float[] _rolls;
        int _count;
        float _radius;
        float _size;
        float _height;
        float _speed;
        bool _ready;

        public void Initialize(Material material, int count, float radius, float size, float height, float speed)
        {
            _count = Mathf.Max(0, count);
            _radius = radius;
            _size = Mathf.Max(0.001f, size);
            _height = height;
            _speed = speed;
            if (_count == 0 || material == null)
            {
                enabled = false;
                return;
            }

            _filter = gameObject.AddComponent<MeshFilter>();
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.sortingOrder = 20;

            _mesh = new Mesh();
            _mesh.name = "MaidHeadStars";
            _mesh.MarkDynamic();
            _filter.sharedMesh = _mesh;

            _vertices = new Vector3[_count * 4];
            _uvs = new Vector2[_count * 4];
            _triangles = new int[_count * 6];
            _radii = new float[_count];
            _heights = new float[_count];
            _rolls = new float[_count];
            System.Random random = new System.Random(114514);
            for (int i = 0; i < _count; i++)
            {
                _radii[i] = _radius + (float)random.NextDouble() * 0.1f;
                _heights[i] = _height + (float)random.NextDouble() * 0.1f;
                _rolls[i] = (float)random.NextDouble() * 0.5f;

                int v = i * 4;
                _uvs[v] = new Vector2(0f, 0f);
                _uvs[v + 1] = new Vector2(1f, 0f);
                _uvs[v + 2] = new Vector2(1f, 1f);
                _uvs[v + 3] = new Vector2(0f, 1f);

                int t = i * 6;
                _triangles[t] = v;
                _triangles[t + 1] = v + 1;
                _triangles[t + 2] = v + 2;
                _triangles[t + 3] = v;
                _triangles[t + 4] = v + 2;
                _triangles[t + 5] = v + 3;
            }

            // 必须先写 vertices，Unity 才允许设置 uv / triangles
            _mesh.vertices = _vertices;
            _mesh.uv = _uvs;
            _mesh.triangles = _triangles;
            _ready = true;
            UpdateMesh();
        }

        void Update()
        {
            if (_ready)
            {
                UpdateMesh();
            }
        }

        void UpdateMesh()
        {
            if (_mesh == null || _vertices == null)
            {
                return;
            }

            float time = Time.time * _speed;
            for (int i = 0; i < _count; i++)
            {
                float angle = time + i * Mathf.PI * 2f / _count;
                float x = Mathf.Cos(angle) * _radii[i];
                float z = Mathf.Sin(angle) * _radii[i];
                Vector3 center = new Vector3(x, _heights[i], z);
                Vector3 normal = new Vector3(-x, 0f, -z).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, normal);
                if (right.sqrMagnitude < 0.0001f)
                {
                    right = Vector3.right;
                }
                else
                {
                    right.Normalize();
                }

                Vector3 up = Vector3.Cross(normal, right).normalized;
                Quaternion roll = Quaternion.AngleAxis(_rolls[i] * Mathf.Rad2Deg, normal);
                right = roll * right;
                up = roll * up;

                int v = i * 4;
                _vertices[v] = center - right * _size - up * _size;
                _vertices[v + 1] = center + right * _size - up * _size;
                _vertices[v + 2] = center + right * _size + up * _size;
                _vertices[v + 3] = center - right * _size + up * _size;
            }

            _mesh.vertices = _vertices;
            _mesh.RecalculateBounds();
        }

        void OnDestroy()
        {
            if (_mesh != null)
            {
                Destroy(_mesh);
                _mesh = null;
            }
        }
    }
}
