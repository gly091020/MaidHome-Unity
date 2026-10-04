using MaidHome.Interop.Bedrock;
using MaidHome.Interop.Maid;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 把加载好的女仆放进世界：摆位置 + 补齐动画和行走需要的组件。
    /// 以后玩家选"留在背包"时，就只是不调这里，assets 继续收着。
    /// </summary>
    public static class MaidPlacement
    {
        public static GameObject Place(MaidAssets assets, Vector3 position, Quaternion rotation)
        {
            if (assets == null || assets.Root == null)
            {
                Debug.LogError("女仆资源是空的，先走 MaidLoader.Load");
                return null;
            }

            GameObject root = assets.Root;
            root.transform.SetPositionAndRotation(position, rotation);
            root.SetActive(true);

            Animation animation = root.GetComponent<Animation>();
            if (animation == null)
            {
                root.AddComponent<Animation>();
            }

            BedrockAnimationPlayer player = root.GetComponent<BedrockAnimationPlayer>();
            if (player == null)
            {
                player = root.AddComponent<BedrockAnimationPlayer>();
            }

            player.SetClips(assets.Clips, assets.ClipData);

            CharacterController controller = root.GetComponent<CharacterController>();
            if (controller == null)
            {
                controller = root.AddComponent<CharacterController>();
                // 模型根在脚底，胶囊中心要抬到身高的一半
                controller.height = 2.5f;
                controller.radius = 0.5f;
                controller.center = new Vector3(0f, 1.3f, 0f);
                controller.slopeLimit = 60f;
                controller.stepOffset = 0.4f;
            }

            bool simpleBedrock = assets.Maid != null && assets.Maid.SimpleBedrockModel;
            if (!simpleBedrock && assets.Clips.Count == 0 && MaidSimpleBedrockAnimator.HasDefaultRig(root.transform))
            {
                // 模组那边还有一类模型是 Java 代码统一驱动的（没有 animation.json），
                // Unity 这边也一样：没有动画表 + 有那套四肢骨骼，就用同一套程序化动画驱动，别再报"没有走路动画"
                simpleBedrock = true;
            }

            MaidSimpleBedrockAnimator simpleAnimator = root.GetComponent<MaidSimpleBedrockAnimator>();
            if (simpleBedrock)
            {
                if (simpleAnimator == null)
                {
                    root.AddComponent<MaidSimpleBedrockAnimator>();
                }
            }
            else if (simpleAnimator != null)
            {
                simpleAnimator.enabled = false;
            }

            if (root.GetComponent<MaidWanderer>() == null)
            {
                root.AddComponent<MaidWanderer>();
            }

            // 模型里有 blink（闭眼贴片）才用得上，没有的话这个组件是空操作
            if (root.GetComponent<MaidHurtBlink>() == null)
            {
                root.AddComponent<MaidHurtBlink>();
            }

            MaidAgent agent = root.GetComponent<MaidAgent>();
            if (agent == null)
            {
                agent = root.AddComponent<MaidAgent>();
            }

            agent.Id = assets.Maid != null ? assets.Maid.Id : "";
            agent.Save = assets.Maid;

            return root;
        }
    }
}
