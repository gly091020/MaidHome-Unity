using UnityEngine;

namespace MaidHome.Interop.Gltf
{
    /// <summary>测试入口：挂到场景里填好路径和缩放，进 Play 就从外部文件加载模型。</summary>
    public sealed class GltfModelSpawner : MonoBehaviour
    {
        [SerializeField] private string _path = "";
        [SerializeField] private float _scale = 1f;
        [SerializeField] private Transform _parent;

        async void Start()
        {
            await GltfModelLoader.LoadAsync(_path, _parent != null ? _parent : transform, _scale);
        }
    }
}
