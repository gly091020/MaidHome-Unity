using MaidHome.Interop.Maid;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>场景里这一只女仆的运行时标记：点选、交互面板和相机取景都从这里拿数据。</summary>
    [DisallowMultipleComponent]
    public sealed class MaidAgent : MonoBehaviour
    {
        public string Id = "";
        public MaidSaveData Save;

        MaidWanderer _wanderer;

        public MaidWanderer Wanderer
        {
            get
            {
                if (_wanderer == null)
                {
                    _wanderer = GetComponent<MaidWanderer>();
                }

                return _wanderer;
            }
        }

        public Bounds GetBounds()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(transform.position + Vector3.up, new Vector3(1f, 2f, 1f));
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }
    }
}
