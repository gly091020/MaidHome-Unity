using System.Collections.Generic;
using MaidHome.Core.Json;
using UnityEngine;

namespace MaidHome.Interop.Bedrock
{
    public struct BedrockFaceUv
    {
        public Vector2 Uv;
        public Vector2 UvSize;
    }

    public sealed class BedrockCube
    {
        public Vector3 Origin;
        public Vector3 Size;
        public Vector3 Rotation;
        public Vector3 Pivot;
        public float Inflate;
        public bool Mirror;
        public bool BoxUv;
        public Vector2 Uv;
        public readonly Dictionary<string, BedrockFaceUv> FaceUvs = new Dictionary<string, BedrockFaceUv>();

        public bool HasRotation
        {
            get { return Rotation.sqrMagnitude > 1e-10f; }
        }
    }

    public sealed class BedrockBone
    {
        public string Name;
        public string Parent;
        public Vector3 Pivot;
        public Vector3 Rotation;
        public float Inflate;
        public readonly List<BedrockCube> Cubes = new List<BedrockCube>();
    }

    public sealed class BedrockGeometry
    {
        public string Identifier = "geometry.unknown";
        public string FormatVersion = "?";
        public float TextureWidth = 64f;
        public float TextureHeight = 64f;
        public readonly List<BedrockBone> Bones = new List<BedrockBone>();
        public readonly List<string> Warnings = new List<string>();

        public static BedrockGeometry ParseFile(string path)
        {
            return Parse(MiniJson.ParseFile(path));
        }

        public static BedrockGeometry Parse(string json)
        {
            return Parse(MiniJson.Parse(json));
        }

        public static BedrockGeometry Parse(JsonValue root)
        {
            BedrockGeometry geometry = new BedrockGeometry();
            geometry.FormatVersion = root["format_version"].ToString();

            JsonValue body = root["minecraft:geometry"];
            if (!body.IsNull)
            {
                if (body.IsArray)
                {
                    body = body[0];
                }

                JsonValue description = body["description"];
                geometry.Identifier = description["identifier"].AsString("geometry.unknown");
                geometry.TextureWidth = description["texture_width"].AsFloat(64f);
                geometry.TextureHeight = description["texture_height"].AsFloat(64f);
                geometry.ReadBones(body["bones"]);
                return geometry;
            }

            foreach (KeyValuePair<string, JsonValue> pair in root.Fields)
            {
                if (!pair.Key.StartsWith("geometry.") || pair.Value.IsNull)
                {
                    continue;
                }

                JsonValue legacy = pair.Value;
                geometry.Identifier = pair.Key;
                geometry.TextureWidth = legacy["texturewidth"].AsFloat(legacy["texture_width"].AsFloat(64f));
                geometry.TextureHeight = legacy["textureheight"].AsFloat(legacy["texture_height"].AsFloat(64f));
                geometry.ReadBones(legacy["bones"]);
                return geometry;
            }

            throw new JsonParseException("文件里没有找到 geometry 数据", 0);
        }

        void ReadBones(JsonValue bones)
        {
            if (bones.IsNull)
            {
                Warnings.Add("模型里没有 bones 数组");
                return;
            }

            for (int i = 0; i < bones.Count; i++)
            {
                JsonValue node = bones[i];
                BedrockBone bone = new BedrockBone();
                bone.Name = node["name"].AsString("bone" + i);
                JsonValue parent = node["parent"];
                bone.Parent = parent.IsNull ? null : parent.AsString(null);
                bone.Pivot = ReadVector3(node["pivot"], Vector3.zero);
                bone.Rotation = ReadVector3(node["rotation"], Vector3.zero);
                bone.Inflate = node["inflate"].AsFloat(0f);

                JsonValue cubes = node["cubes"];
                for (int c = 0; c < cubes.Count; c++)
                {
                    bone.Cubes.Add(ReadCube(cubes[c]));
                }

                Bones.Add(bone);
            }
        }

        static BedrockCube ReadCube(JsonValue node)
        {
            BedrockCube cube = new BedrockCube();
            cube.Origin = ReadVector3(node["origin"], Vector3.zero);
            cube.Size = ReadVector3(node["size"], Vector3.zero);
            cube.Rotation = ReadVector3(node["rotation"], Vector3.zero);
            cube.Pivot = ReadVector3(node["pivot"], Vector3.zero);
            cube.Inflate = node["inflate"].AsFloat(0f);
            cube.Mirror = node["mirror"].AsBool(false);

            JsonValue uv = node["uv"];
            if (uv.IsArray)
            {
                cube.BoxUv = true;
                cube.Uv = new Vector2(uv[0].AsFloat(0f), uv[1].AsFloat(0f));
            }
            else if (uv.IsObject)
            {
                cube.BoxUv = false;
                foreach (KeyValuePair<string, JsonValue> pair in uv.Fields)
                {
                    BedrockFaceUv face = new BedrockFaceUv();
                    face.Uv = ReadVector2(pair.Value["uv"], Vector2.zero);
                    face.UvSize = ReadVector2(pair.Value["uv_size"], new Vector2(cube.Size.x, cube.Size.y));
                    cube.FaceUvs[pair.Key] = face;
                }
            }

            return cube;
        }

        static Vector2 ReadVector2(JsonValue node, Vector2 fallback)
        {
            if (!node.IsArray || node.Count < 2)
            {
                return fallback;
            }

            return new Vector2(node[0].AsFloat(fallback.x), node[1].AsFloat(fallback.y));
        }

        static Vector3 ReadVector3(JsonValue node, Vector3 fallback)
        {
            if (!node.IsArray || node.Count < 3)
            {
                return fallback;
            }

            return new Vector3(node[0].AsFloat(fallback.x), node[1].AsFloat(fallback.y), node[2].AsFloat(fallback.z));
        }
    }
}
