using System.Collections.Generic;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 喂蛋糕用的命中体：现搭 6 个部位的**实体**盒子——头 / 躯干 / 左臂 / 右臂 / 左腿 / 右腿，
    /// 尺寸按模型对应骨骼上的网格算（骨骼名字找不到的部位就跳过，不硬编一个假盒子）。
    /// 头那块同时是「喂到」的判定目标：蛋糕砸到它才算吃到，砸别处只是被挡/弹开。
    /// 每块都跟女仆自己的 CharacterController 互相 IgnoreCollision——她那颗移动胶囊把整个人
    /// （包括头）都包住了，不忽略的话她自己会撞在这些盒子上。
    /// </summary>
    public sealed class MaidFeedHitBoxes
    {
        /// <summary>砸到它才算「喂到」</summary>
        public Collider Head;

        readonly List<GameObject> _holders = new List<GameObject>();

        static readonly string[] HeadNames = { "head", "mhead", "allhead", "head2" };
        static readonly string[] TorsoNames =
        {
            "upbody", "upperbody", "body", "downbody", "allbody", "chest", "torso", "spine"
        };
        // 别加笼统的 "arm" / "leg"：模型里常有 Arm / Leg 这种"左右共用"的父骨骼，
        // 匹配到它两边会算出同一个（整条双臂）盒子
        static readonly string[] LeftArmNames = { "leftarm", "armleft", "larm", "leftforearm" };
        static readonly string[] RightArmNames = { "rightarm", "armright", "rarm", "rightforearm" };
        static readonly string[] LeftLegNames = { "leftleg", "legleft", "lleg", "leftlowerleg" };
        static readonly string[] RightLegNames = { "rightleg", "legright", "rleg" };

        public static MaidFeedHitBoxes Build(MaidAgent agent, MaidFaceRig rig)
        {
            if (agent == null)
            {
                return null;
            }

            List<Transform> all = new List<Transform>();
            CollectBones(agent.transform, all);
            if (all.Count == 0)
            {
                return null;
            }

            MaidFeedHitBoxes boxes = new MaidFeedHitBoxes();
            CharacterController controller = agent.GetComponent<CharacterController>();

            // 头和四肢先找，躯干要用它们把范围扣掉
            Transform head = FindTopBone(all, HeadNames);
            Transform leftArm = FindTopBone(all, LeftArmNames);
            Transform rightArm = FindTopBone(all, RightArmNames);
            Transform leftLeg = FindTopBone(all, LeftLegNames);
            Transform rightLeg = FindTopBone(all, RightLegNames);

            Bounds bounds;
            if (TryGetHeadBounds(agent, rig, head, out bounds))
            {
                boxes.Head = boxes.AddBox(agent, head, "Head", bounds, controller);
            }

            if (TryGetSubtreeBounds(leftArm, out bounds))
            {
                boxes.AddBox(agent, leftArm, "ArmLeft", bounds, controller);
            }

            if (TryGetSubtreeBounds(rightArm, out bounds))
            {
                boxes.AddBox(agent, rightArm, "ArmRight", bounds, controller);
            }

            if (TryGetSubtreeBounds(leftLeg, out bounds))
            {
                boxes.AddBox(agent, leftLeg, "LegLeft", bounds, controller);
            }

            if (TryGetSubtreeBounds(rightLeg, out bounds))
            {
                boxes.AddBox(agent, rightLeg, "LegRight", bounds, controller);
            }

            if (TryGetTorsoBounds(agent, all, head, leftArm, rightArm, leftLeg, rightLeg, out bounds))
            {
                boxes.AddBox(agent, null, "Torso", bounds, controller);
            }

            return boxes;
        }

        public void Destroy()
        {
            Head = null;
            for (int i = 0; i < _holders.Count; i++)
            {
                if (_holders[i] != null)
                {
                    Object.Destroy(_holders[i]);
                }
            }

            _holders.Clear();
        }

        Collider AddBox(MaidAgent agent, Transform parent, string part, Bounds worldBounds,
            CharacterController controller)
        {
            GameObject holder = new GameObject("MaidFeedHit " + part);
            holder.transform.SetParent(parent != null ? parent : agent.transform, false);
            // 包围盒是世界空间的轴对齐盒，所以盒子也按世界轴摆，再换算成父物体本地尺寸
            holder.transform.position = worldBounds.center;
            holder.transform.rotation = Quaternion.identity;

            BoxCollider box = holder.AddComponent<BoxCollider>();
            box.isTrigger = false;
            Vector3 scale = holder.transform.lossyScale;
            box.size = new Vector3(
                SafeDivide(Mathf.Max(0.05f, worldBounds.size.x), scale.x),
                SafeDivide(Mathf.Max(0.05f, worldBounds.size.y), scale.y),
                SafeDivide(Mathf.Max(0.05f, worldBounds.size.z), scale.z));

            if (controller != null)
            {
                // 只挡蛋糕，别挡她自己的移动
                Physics.IgnoreCollision(box, controller, true);
            }

            _holders.Add(holder);
            return box;
        }

        static bool TryGetHeadBounds(MaidAgent agent, MaidFaceRig rig, Transform head, out Bounds bounds)
        {
            bounds = new Bounds();
            if (rig != null && rig.HeadMesh != null)
            {
                bounds = rig.HeadMesh.bounds;
                return true;
            }

            if (TryGetSubtreeBounds(head, out bounds))
            {
                return true;
            }

            // 连头骨骼都没有（方块模型那种）：拿整体上半段估一个
            if (!TryGetActiveBounds(agent.transform, out bounds))
            {
                return false;
            }

            bounds = new Bounds(bounds.center + Vector3.up * (bounds.extents.y * 0.35f),
                new Vector3(bounds.size.x, bounds.size.y * 0.4f, bounds.size.z));
            return true;
        }

        /// <summary>躯干：先取躯干骨骼自己那几块网格；一块都没有就退化成"整体减掉头四肢"</summary>
        static bool TryGetTorsoBounds(MaidAgent agent, List<Transform> all, Transform head, Transform leftArm,
            Transform rightArm, Transform leftLeg, Transform rightLeg, out Bounds bounds)
        {
            bounds = new Bounds();
            bool any = false;
            for (int i = 0; i < all.Count; i++)
            {
                if (!Matches(all[i].name, TorsoNames))
                {
                    continue;
                }

                Renderer renderer = all[i].GetComponent<Renderer>();
                if (renderer == null)
                {
                    continue;
                }

                Encapsulate(ref bounds, renderer.bounds, ref any);
            }

            if (any)
            {
                return true;
            }

            Renderer[] renderers = agent.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Transform bone = renderers[i].transform;
                if (IsUnder(bone, head) || IsUnder(bone, leftArm) || IsUnder(bone, rightArm)
                    || IsUnder(bone, leftLeg) || IsUnder(bone, rightLeg))
                {
                    continue;
                }

                Encapsulate(ref bounds, renderers[i].bounds, ref any);
            }

            return any;
        }

        static void Encapsulate(ref Bounds bounds, Bounds add, ref bool any)
        {
            if (!any)
            {
                bounds = add;
                any = true;
                return;
            }

            bounds.Encapsulate(add);
        }

        static bool TryGetSubtreeBounds(Transform bone, out Bounds bounds)
        {
            bounds = new Bounds();
            if (bone == null)
            {
                return false;
            }

            Renderer[] renderers = bone.GetComponentsInChildren<Renderer>();
            bool any = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Encapsulate(ref bounds, renderers[i].bounds, ref any);
            }

            return any;
        }

        static bool TryGetActiveBounds(Transform root, out Bounds bounds)
        {
            bounds = new Bounds();
            if (root == null)
            {
                return false;
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            bool any = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Encapsulate(ref bounds, renderers[i].bounds, ref any);
            }

            return any;
        }

        static void CollectBones(Transform node, List<Transform> into)
        {
            if (node == null || !node.gameObject.activeInHierarchy)
            {
                return;
            }

            into.Add(node);
            for (int i = 0; i < node.childCount; i++)
            {
                CollectBones(node.GetChild(i), into);
            }
        }

        /// <summary>名字匹配里取最靠上的那根（骨骼链前面那根才是整条手臂/整条腿）</summary>
        static Transform FindTopBone(List<Transform> all, string[] names)
        {
            Transform found = null;
            int bestDepth = int.MaxValue;
            for (int i = 0; i < all.Count; i++)
            {
                if (!Matches(all[i].name, names))
                {
                    continue;
                }

                int depth = Depth(all[i]);
                if (depth < bestDepth)
                {
                    bestDepth = depth;
                    found = all[i];
                }
            }

            return found;
        }

        static int Depth(Transform node)
        {
            int depth = 0;
            Transform current = node;
            while (current != null)
            {
                depth++;
                current = current.parent;
            }

            return depth;
        }

        static bool IsUnder(Transform node, Transform ancestor)
        {
            if (node == null || ancestor == null)
            {
                return false;
            }

            Transform current = node;
            int guard = 0;
            while (current != null && guard++ < 64)
            {
                if (current == ancestor)
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        static bool Matches(string name, string[] names)
        {
            string normalized = Normalize(name);
            if (normalized.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < names.Length; i++)
            {
                if (normalized == names[i])
                {
                    return true;
                }
            }

            return false;
        }

        static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "";
            }

            return name.ToLowerInvariant()
                .Replace("_", "")
                .Replace("-", "")
                .Replace(".", "")
                .Replace(" ", "");
        }

        static float SafeDivide(float value, float scale)
        {
            return Mathf.Abs(scale) > 0.0001f ? value / Mathf.Abs(scale) : value;
        }
    }
}
