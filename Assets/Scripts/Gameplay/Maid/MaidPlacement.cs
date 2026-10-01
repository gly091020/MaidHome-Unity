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

            player.SetClips(assets.Clips);

            CharacterController controller = root.GetComponent<CharacterController>();
            if (controller == null)
            {
                controller = root.AddComponent<CharacterController>();
                // 模型根在脚底，胶囊中心要抬到身高的一半
                controller.height = 1.75f;
                controller.radius = 0.3f;
                controller.center = new Vector3(0f, 0.9f, 0f);
                controller.slopeLimit = 60f;
                controller.stepOffset = 0.4f;
            }

            bool simpleBedrock = assets.Maid != null && assets.Maid.SimpleBedrockModel;
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
