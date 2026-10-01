using GLTFast;
using UnityEngine;

namespace MaidHome.Interop.Gltf
{
    /// <summary>
    /// glTFast 实例化出来的网格/贴图/材质都挂在那次 GltfImport 上，Dispose 会把它们一起销毁。
    /// 所以模型活着的时候必须有人拿着 import，等模型销毁再 Dispose。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GltfImportHolder : MonoBehaviour
    {
        GltfImport _import;

        public void Hold(GltfImport import)
        {
            _import = import;
        }

        void OnDestroy()
        {
            if (_import != null)
            {
                _import.Dispose();
                _import = null;
            }
        }
    }
}
