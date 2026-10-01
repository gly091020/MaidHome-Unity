using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace MaidHome.EditorTools
{
    /// <summary>
    /// 把生成出来的模型写成 OBJ，坐标换算成 Blockbench 的内部坐标系（bb），
    /// 这样能和 Blockbench 自己导出的 OBJ 直接对比：bb = (x*16, y*16, -z*16)。
    /// 方块顶点按位置去重，所以一根骨骼下一般是每块 8 个 v。
    /// </summary>
    public static class BedrockModelObjWriter
    {
        public static int Write(GameObject root, string path)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("# Made in MaidHome (Unity)");

            List<Vector3> positions = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<Vector3> normals = new List<Vector3>();
            List<int> perVertexIndex = new List<int>();
            List<int> perVertexNormal = new List<int>();

            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
            int vertexOffset = 0;
            int uvOffset = 0;

            for (int f = 0; f < filters.Length; f++)
            {
                Mesh mesh = filters[f].sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                Transform bone = filters[f].transform;
                text.AppendLine("o " + bone.name);

                Vector3[] meshVertices = mesh.vertices;
                Vector2[] meshUvs = mesh.uv;
                Vector3[] meshNormals = mesh.normals;
                int[] meshTriangles = mesh.triangles;

                List<Vector3> localPositions = new List<Vector3>();
                int[] remap = new int[meshVertices.Length];
                for (int i = 0; i < meshVertices.Length; i++)
                {
                    Vector3 world = bone.TransformPoint(meshVertices[i]);
                    Vector3 bb = new Vector3(world.x * 16f, world.y * 16f, -world.z * 16f);
                    int found = -1;
                    for (int k = 0; k < localPositions.Count; k++)
                    {
                        if ((localPositions[k] - bb).sqrMagnitude < 1e-8f)
                        {
                            found = k;
                            break;
                        }
                    }
                    if (found < 0)
                    {
                        found = localPositions.Count;
                        localPositions.Add(bb);
                    }
                    remap[i] = found;
                }

                for (int i = 0; i < localPositions.Count; i++)
                {
                    Vector3 p = localPositions[i];
                    text.AppendLine("v " + F(p.x) + " " + F(p.y) + " " + F(p.z));
                    positions.Add(p);
                }

                for (int i = 0; i < meshVertices.Length; i++)
                {
                    Vector2 uv = i < meshUvs.Length ? meshUvs[i] : Vector2.zero;
                    text.AppendLine("vt " + F(uv.x) + " " + F(uv.y));
                    uvs.Add(uv);
                }

                for (int i = 0; i < meshVertices.Length; i++)
                {
                    Vector3 n = i < meshNormals.Length ? meshNormals[i] : Vector3.up;
                    Vector3 world = bone.TransformDirection(n);
                    Vector3 bb = new Vector3(world.x, world.y, -world.z).normalized;
                    int found = -1;
                    for (int k = 0; k < normals.Count; k++)
                    {
                        if ((normals[k] - bb).sqrMagnitude < 1e-8f)
                        {
                            found = k;
                            break;
                        }
                    }
                    if (found < 0)
                    {
                        found = normals.Count;
                        normals.Add(bb);
                        text.AppendLine("vn " + F(bb.x) + " " + F(bb.y) + " " + F(bb.z));
                    }
                    perVertexIndex.Add(remap[i]);
                    perVertexNormal.Add(found);
                }

                for (int i = 0; i + 2 < meshTriangles.Length; i += 3)
                {
                    text.Append("f");
                    for (int k = 0; k < 3; k++)
                    {
                        int vi = meshTriangles[i + k];
                        text.Append(" " + (vertexOffset + perVertexIndex[vi] + 1)
                            + "/" + (uvOffset + vi + 1)
                            + "/" + (perVertexNormal[vi] + 1));
                    }
                    text.AppendLine();
                }

                vertexOffset += localPositions.Count;
                uvOffset += meshVertices.Length;
            }

            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
            return positions.Count;
        }

        static string F(float value)
        {
            return value.ToString("0.#####", CultureInfo.InvariantCulture);
        }
    }
}
