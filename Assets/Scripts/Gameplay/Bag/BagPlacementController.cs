using System;
using MaidHome.Core.Input;
using MaidHome.Gameplay.House;
using UnityEngine;

namespace MaidHome.Gameplay.Bag
{
    /// <summary>
    /// 通用放置模式：从背包里点了“取出”后进入，手指拖到房子的可走格上，点底部的「放置」确认。
    /// 朝向和取消都挂在底部按钮条上，输入统一走 PointerInput。
    /// 将来家具/道具也能复用这里，只要求 provider 自己知道怎么落地。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BagPlacementController : MonoBehaviour
    {
        public static BagPlacementController Instance { get; private set; }

        [Tooltip("点一次「旋转」转多少度")]
        [SerializeField] private float _yawStep = 45f;
        [SerializeField] private float _markerSize = 0.9f;
        [SerializeField] private Color _validColor = new Color(0.2f, 1f, 0.3f, 0.55f);
        [SerializeField] private Color _invalidColor = new Color(1f, 0.25f, 0.2f, 0.55f);

        public bool IsActive { get; private set; }

        Action<Vector3, float> _onPlaced;
        Action _onCancel;
        BagPlacementPanel _panel;
        GameObject _marker;
        Material _markerMaterial;
        Vector3 _feet;
        float _yaw = 180f;
        bool _valid;
        bool _hasAim;
        bool _aiming;

        void Awake()
        {
            Instance = this;

            _panel = GetComponent<BagPlacementPanel>();
            if (_panel == null)
            {
                _panel = gameObject.AddComponent<BagPlacementPanel>();
            }

            _panel.RotateRequested += OnRotateRequested;
            _panel.CancelRequested += Cancel;
            _panel.ConfirmRequested += Confirm;
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (_panel != null)
            {
                _panel.RotateRequested -= OnRotateRequested;
                _panel.CancelRequested -= Cancel;
                _panel.ConfirmRequested -= Confirm;
            }

            if (_marker != null)
            {
                Destroy(_marker);
            }

            if (_markerMaterial != null)
            {
                Destroy(_markerMaterial);
            }
        }

        public bool Begin(string kind, string id, Action<Vector3, float> onPlaced, Action onCancel)
        {
            if (onPlaced == null || !HouseContext.HasHouse)
            {
                Debug.LogWarning("没有可放置的房子");
                return false;
            }

            _onPlaced = onPlaced;
            _onCancel = onCancel;
            _yaw = 180f;
            _aiming = false;
            _hasAim = false;
            IsActive = true;
            EnsureMarker();
            if (_panel != null)
            {
                _panel.Show();
            }

            return true;
        }

        public void Cancel()
        {
            Cancel(true);
        }

        void Cancel(bool notify)
        {
            bool wasActive = IsActive;
            IsActive = false;
            Action onCancel = _onCancel;
            _onPlaced = null;
            _onCancel = null;
            _aiming = false;
            _hasAim = false;
            if (_marker != null)
            {
                _marker.SetActive(false);
            }

            if (_panel != null)
            {
                _panel.Hide();
            }

            if (notify && wasActive && onCancel != null)
            {
                onCancel();
            }
        }

        void Update()
        {
            if (!IsActive)
            {
                return;
            }

            // Android 返回键 / 电脑 Esc
            if (PointerInput.BackPressed)
            {
                Cancel();
                return;
            }

            PointerInput.Pointer pointer = PointerInput.Primary;
            if (pointer.Pressed)
            {
                // 手指是从按钮条上按下去的这一段手势就不算瞄准，免得点「旋转」顺手把标记挪走
                _aiming = !pointer.OverUi;
            }

            if (!_aiming)
            {
                return;
            }

            if (pointer.Held)
            {
                Aim(pointer.Position);
                return;
            }

            if (pointer.Released)
            {
                _aiming = false;
                Aim(pointer.Position);
            }
        }

        void OnRotateRequested()
        {
            RotateBy(_yawStep);
        }

        /// <summary>转放置预览的朝向，按钮和以后的快捷键都走这里。</summary>
        public void RotateBy(float degrees)
        {
            if (!IsActive)
            {
                return;
            }

            _yaw += degrees;
            ApplyMarkerPose();
        }

        /// <summary>确认放下。位置无效时不放，按钮和返回键之外都靠这里收尾。</summary>
        public void Confirm()
        {
            if (!IsActive)
            {
                return;
            }

            if (!_hasAim || !_valid)
            {
                Debug.LogWarning("只能放在可走的位置");
                return;
            }

            Action<Vector3, float> callback = _onPlaced;
            Vector3 feet = _feet;
            float yaw = _yaw;
            Cancel(false);
            if (callback != null)
            {
                callback(feet, yaw);
            }
        }

        /// <summary>把标记挪到手指底下的可走格上。触摸没有 hover，所以只在按住时更新。</summary>
        void Aim(Vector2 screenPosition)
        {
            _valid = false;
            _hasAim = false;
            HouseGridView view = HouseContext.View;
            Camera camera = Camera.main;
            if (view == null || camera == null)
            {
                ApplyMarkerPose();
                return;
            }

            Ray ray = camera.ScreenPointToRay(screenPosition);
            RaycastHit[] hits = Physics.RaycastAll(ray, 500f);
            Array.Sort(hits, CompareHits);

            for (int i = 0; i < hits.Length; i++)
            {
                HouseGridView hitView = hits[i].collider.GetComponentInParent<HouseGridView>();
                if (hitView == null)
                {
                    continue;
                }

                _hasAim = true;
                _feet = hits[i].point;
                Vector3Int cell;
                bool walkable;
                if (hitView.TryWorldToCell(hits[i].point, out cell, out walkable))
                {
                    _valid = walkable;
                    if (walkable)
                    {
                        _feet = hitView.CellFeet(cell);
                    }
                }

                break;
            }

            ApplyMarkerPose();
        }

        void ApplyMarkerPose()
        {
            if (_marker == null)
            {
                return;
            }

            _marker.SetActive(_hasAim);
            if (!_hasAim)
            {
                return;
            }

            _marker.transform.position = _feet + Vector3.up * 0.03f;
            _marker.transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (_markerMaterial != null)
            {
                _markerMaterial.color = _valid ? _validColor : _invalidColor;
            }
        }

        static int CompareHits(RaycastHit a, RaycastHit b)
        {
            return a.distance.CompareTo(b.distance);
        }

        void EnsureMarker()
        {
            if (_marker != null)
            {
                return;
            }

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            _markerMaterial = new Material(shader);
            _markerMaterial.color = _validColor;

            // 根节点不缩放，两个部件各自缩放：圆柱看不出朝向，前面那根短条标出女仆朝哪边
            _marker = new GameObject("BagPlacementMarker");
            AddMarkerPart(PrimitiveType.Cylinder, "Disc",
                new Vector3(_markerSize, 0.01f, _markerSize), Vector3.zero);
            AddMarkerPart(PrimitiveType.Cube, "Facing",
                new Vector3(_markerSize * 0.2f, 0.01f, _markerSize * 0.45f),
                new Vector3(0f, 0f, _markerSize * 0.4f));

            _marker.SetActive(false);
        }

        void AddMarkerPart(PrimitiveType type, string partName, Vector3 scale, Vector3 localPosition)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = partName;
            part.transform.SetParent(_marker.transform, false);
            part.transform.localScale = scale;
            part.transform.localPosition = localPosition;

            Collider collider = part.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = _markerMaterial;
            }
        }
    }
}
