using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 受伤闪红：把模型材质的乘色（_Color）整段乘成纯红，再整段复位。
    /// 对齐原版：受伤实体是 texture × (1,0,0)（只剩红通道），持续 hurtTime = 10 tick = 0.5 秒，
    /// 中间不淡出，到点直接恢复。只有视觉效果，不掉血。
    /// 女仆的材质是每个模型实例单独建的，所以只影响这一只。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidDamageFlash : MonoBehaviour
    {
        [SerializeField] private Color _color = new Color(1f, 0f, 0f, 1f);
        [SerializeField] private float _seconds = 0.5f;

        Material[] _materials;
        Color[] _baseColors;
        Coroutine _routine;

        /// <summary>给某个物体闪一下红，没组件就自己加一个</summary>
        public static void Flash(GameObject target, Color color, float seconds)
        {
            if (target == null)
            {
                return;
            }

            MaidDamageFlash flash = target.GetComponent<MaidDamageFlash>();
            if (flash == null)
            {
                flash = target.AddComponent<MaidDamageFlash>();
            }

            flash.Play(color, seconds);
        }

        public void Play(Color color, float seconds)
        {
            // 方块模型没有 attacked 动画可借，用它们自己的 blink 节点闭一下眼当受伤表现
            MaidHurtBlink blink = GetComponent<MaidHurtBlink>();
            if (blink != null && blink.isActiveAndEnabled)
            {
                blink.Play();
            }

            if (_routine != null)
            {
                StopCoroutine(_routine);
                Restore();
            }

            if (!Capture())
            {
                return;
            }

            _color = color;
            _seconds = Mathf.Max(0.05f, seconds);
            _routine = StartCoroutine(Run());
        }

        void OnDisable()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            Restore();
        }

        IEnumerator Run()
        {
            Apply(_color);
            float elapsed = 0f;
            while (elapsed < _seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            Restore();
            _routine = null;
        }

        bool Capture()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            List<Material> materials = new List<Material>();
            List<Color> colors = new List<Color>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Material material = renderers[i].sharedMaterial;
                if (material == null || !material.HasProperty("_Color") || materials.Contains(material))
                {
                    continue;
                }

                materials.Add(material);
                colors.Add(material.GetColor("_Color"));
            }

            _materials = materials.ToArray();
            _baseColors = colors.ToArray();
            return _materials.Length > 0;
        }

        void Apply(Color color)
        {
            // 材质的 alpha 是拿来裁剪的不能动，所以把 _flashColor 的 alpha 当成"闪的强度"：
            // a=1 → 完全乘成这个颜色（原版受伤那样），a=0.3 → 只乘三成（半透明红），a=0 → 不闪
            float strength = Mathf.Clamp01(color.a);
            for (int i = 0; i < _materials.Length; i++)
            {
                if (_materials[i] != null)
                {
                    Color baseColor = _baseColors[i];
                    Color tint = baseColor;
                    tint.r = Mathf.Lerp(baseColor.r, baseColor.r * color.r, strength);
                    tint.g = Mathf.Lerp(baseColor.g, baseColor.g * color.g, strength);
                    tint.b = Mathf.Lerp(baseColor.b, baseColor.b * color.b, strength);
                    tint.a = baseColor.a;
                    _materials[i].SetColor("_Color", tint);
                }
            }
        }

        void Restore()
        {
            if (_materials == null)
            {
                return;
            }

            for (int i = 0; i < _materials.Length; i++)
            {
                if (_materials[i] != null)
                {
                    _materials[i].SetColor("_Color", _baseColors[i]);
                }
            }
        }
    }
}
