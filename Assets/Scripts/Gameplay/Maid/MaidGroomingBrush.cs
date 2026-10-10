using System.Collections.Generic;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 梳毛时贴在毛表面上的那把刷子（照抄模组 GroomingBrushRender 的摆放逻辑）。
    /// 原模组世界里那把刷子**不是模型文件**，是代码里现画的几个盒子（柄 + 刷头 + 3×3 刷毛），
    /// 只有背包图标才是一张 16×16 贴图；所以这里也照它用代码生成网格，不需要美术资源。
    /// 想换成自己的模型：把预制体/模型拖到 Model 上，模型优先，代码那份就不用了。
    /// 摆放规则：刷子朝毛流方向（forward），刷面贴着皮肤法线（up）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidGroomingBrush : MonoBehaviour
    {
        [Tooltip("刷子模型 / 预制体。留空 = 用代码画的模组同款小刷子")]
        [SerializeField] private Transform _model;
        [Tooltip("Model 没接时用代码画一把（照抄模组的盒子与配色）")]
        [SerializeField] private bool _codeFallback = true;
        [Tooltip("相对命中点沿法线抬多少，免得和毛表面穿模")]
        [SerializeField] private Vector3 _offset = new Vector3(0f, 0f, 0f);
        [SerializeField] private float _scale = 1f;

        Transform _instance;

        public bool HasModel
        {
            get { return _model != null || _codeFallback; }
        }

        public void Show(Vector3 point, Vector3 normal, Vector3 flow)
        {
            if (!HasModel)
            {
                Hide();
                return;
            }

            Transform instance = Ensure();
            if (instance == null)
            {
                return;
            }

            // 毛流投到切平面上当朝向，法线当 up：刷子就是贴着毛面顺着毛放
            Vector3 tangent = Vector3.ProjectOnPlane(flow, normal);
            if (tangent.sqrMagnitude < 1e-8f)
            {
                tangent = Vector3.ProjectOnPlane(Vector3.forward, normal);
            }

            if (tangent.sqrMagnitude < 1e-8f)
            {
                tangent = Vector3.right;
            }

            Quaternion rotation = Quaternion.LookRotation(tangent.normalized, normal);
            instance.rotation = rotation;
            instance.position = point + rotation * _offset;
            instance.localScale = Vector3.one * Mathf.Max(0.001f, _scale);
            if (!instance.gameObject.activeSelf)
            {
                instance.gameObject.SetActive(true);
            }
        }

        public void Hide()
        {
            if (_instance != null)
            {
                _instance.gameObject.SetActive(false);
            }
        }

        Transform Ensure()
        {
            if (_instance != null)
            {
                return _instance;
            }

            if (_model != null)
            {
                _instance = Instantiate(_model, transform);
                _instance.name = _model.name + " (GroomingBrush)";
            }
            else if (_codeFallback)
            {
                _instance = BuildCodeBrush();
            }

            if (_instance != null)
            {
                _instance.gameObject.SetActive(false);
            }

            return _instance;
        }

        // ---------- 代码画的那把刷子（照抄 GroomingBrushRender.draw） ----------

        /// <summary>
        /// 模组里三个盒子（柄 / 刷头）+ 3×3 刷毛，坐标就是世界尺寸（MC 1 格 = 1 米）：
        /// x = 侧面、y = 法线（贴着皮肤那侧）、z = 毛流方向。配色也是模组里写死的那三组。
        /// </summary>
        Transform BuildCodeBrush()
        {
            Mesh mesh = BuildBrushMesh();
            if (mesh == null)
            {
                return null;
            }

            GameObject holder = new GameObject("GroomingBrush");
            holder.transform.SetParent(transform, false);
            MeshFilter filter = holder.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = holder.AddComponent<MeshRenderer>();

            Shader shader = Shader.Find("MaidHome/MinecraftBlock");
            if (shader == null)
            {
                // 这个 shader 注册在 GraphicsSettings 的 Always Included Shaders 里，正常不会丢
                Debug.LogWarning("找不到 MaidHome/MinecraftBlock，代码画的刷子不显示", this);
                Destroy(holder);
                return null;
            }

            // 顶点色就是配色，所以材质的乘色留白；盒子是现搭的，剔除关掉免得绕序反了看不见
            Material material = new Material(shader);
            material.SetColor("_Color", Color.white);
            material.SetFloat("_Cull", 0f);
            renderer.sharedMaterial = material;
            return holder.transform;
        }

        static Mesh BuildBrushMesh()
        {
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Color> colors = new List<Color>();
            List<int> triangles = new List<int>();

            // 柄：x ±0.065 / y 0.026~0.065 / z -0.075~0.075
            Box(vertices, normals, colors, triangles, new Vector3(-0.065f, 0.026f, -0.075f),
                new Vector3(0.065f, 0.065f, 0.075f), new Color32(128, 76, 39, 255));
            // 刷头：x ±0.022 / y 0.040~0.068 / z 0.070~0.200
            Box(vertices, normals, colors, triangles, new Vector3(-0.022f, 0.040f, 0.070f),
                new Vector3(0.022f, 0.068f, 0.200f), new Color32(172, 111, 57, 255));
            // 刷毛 3×3：x ±0.012 / y 0.004~0.028 / z ±0.014
            for (int x = -1; x <= 1; x++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    float cx = x * 0.04f;
                    float cz = z * 0.045f;
                    Box(vertices, normals, colors, triangles,
                        new Vector3(cx - 0.012f, 0.004f, cz - 0.014f),
                        new Vector3(cx + 0.012f, 0.028f, cz + 0.014f), new Color32(235, 220, 163, 255));
                }
            }

            if (vertices.Count == 0)
            {
                return null;
            }

            Mesh mesh = new Mesh();
            mesh.name = "GroomingBrushMesh";
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>一个轴对齐盒子：六个面各自四个顶点（法线朝外，和模组的角点表同一个顺序）</summary>
        static void Box(List<Vector3> vertices, List<Vector3> normals, List<Color> colors, List<int> triangles,
            Vector3 min, Vector3 max, Color color)
        {
            Vector3 center = (min + max) * 0.5f;
            int[][] corners =
            {
                new[] { 0, 2, 6, 4 },
                new[] { 1, 5, 7, 3 },
                new[] { 0, 4, 5, 1 },
                new[] { 2, 3, 7, 6 },
                new[] { 0, 1, 3, 2 },
                new[] { 4, 6, 7, 5 }
            };

            for (int f = 0; f < corners.Length; f++)
            {
                int baseIndex = vertices.Count;
                Vector3 faceNormal = Vector3.zero;
                for (int i = 0; i < 4; i++)
                {
                    int k = corners[f][i];
                    Vector3 point = new Vector3((k & 1) == 0 ? min.x : max.x,
                        (k & 2) == 0 ? min.y : max.y,
                        (k & 4) == 0 ? min.z : max.z);
                    vertices.Add(point);
                    colors.Add(color);
                    normals.Add(Vector3.zero);
                }

                faceNormal = Vector3.Cross(vertices[baseIndex + 1] - vertices[baseIndex],
                    vertices[baseIndex + 2] - vertices[baseIndex]);
                if (faceNormal.sqrMagnitude < 1e-12f)
                {
                    continue;
                }

                faceNormal.Normalize();
                // 叉积的朝向随坐标系，这里直接按"离盒子中心"校正，保证法线朝外
                if (Vector3.Dot(faceNormal, vertices[baseIndex] - center) < 0f)
                {
                    faceNormal = -faceNormal;
                }

                for (int i = 0; i < 4; i++)
                {
                    normals[baseIndex + i] = faceNormal;
                }

                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex + 3);
            }
        }
    }
}
