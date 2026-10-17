using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>神必音效配置；在 Assets/Resources/Settings/MaidMysterySoundConfig.asset 的 Inspector 里填两条 AudioClip。</summary>
    [CreateAssetMenu(menuName = "MaidHome/神必音效配置", fileName = "MaidMysterySoundConfig")]
    public sealed class MaidMysterySoundConfig : ScriptableObject
    {
        [Tooltip("普通受伤额外音效（布娃娃除外）")]
        [SerializeField] private AudioClip _hurt;
        [Tooltip("布娃娃受伤额外音效")]
        [SerializeField] private AudioClip _ragdoll;

        public AudioClip Hurt
        {
            get { return _hurt; }
        }

        public AudioClip Ragdoll
        {
            get { return _ragdoll; }
        }
    }
}
