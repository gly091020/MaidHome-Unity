using System;
using MaidHome.Gameplay.House;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MaidHome.Gameplay.Bag
{
    /// <summary>
    /// 通用放置模式：从背包里点了“取出”后进入，鼠标指到房子的可走格上确认。
    /// 将来家具/道具也能复用这里，只要求 provider 自己知道怎么落地。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BagPlacementController : MonoBehaviour
    {
        public static BagPlacementController Instance { get; private set; }

        [SerializeField] private float _yawStep = 15f;
        [SerializeField] private float _markerSize = 0.9f;
        [SerializeField] private Color _validColor = new Color(0.2f, 1f, 0.3f, 0.55f);
        [SerializeField] private Color _invalidColor = new Color(1f, 0.25f, 0.2f, 0.55f);

        public bool IsActive { get; private set; }

        Action<Vector3, float> _onPlaced;
        Action _onCancel;
        GameObject _marker;
        Material _markerMaterial;
        Vector3 _feet;
        float _yaw = 180f;
        bool _valid;

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
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
            IsActive = true;
            EnsureMarker();
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
            if (_marker != null)
            {
                _marker.SetActive(false);
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

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cancel();
                return;
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                _yaw += Mathf.Sign(scroll) * _yawStep;
            }

            UpdateCursor();

            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (overUI)
            {
                return;
            }

            if (Input.GetMouseButtonDown(1))
            {
                Cancel();
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                if (!_valid)
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
        }

        void UpdateCursor()
        {
            _valid = false;
            HouseGridView view = HouseContext.View;
            Camera camera = Camera.main;
            if (view == null || camera == null)
            {
                if (_marker != null)
                {
                    _marker.SetActive(false);
                }

                return;
            }

            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            RaycastHit[] hits = Physics.RaycastAll(ray, 500f);
            Array.Sort(hits, CompareHits);

            bool hasPoint = false;
            for (int i = 0; i < hits.Length; i++)
            {
                HouseGridView hitView = hits[i].collider.GetComponentInParent<HouseGridView>();
                if (hitView == null)
                {
                    continue;
                }

                hasPoint = true;
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

            if (_marker == null)
            {
                return;
            }

            _marker.SetActive(hasPoint);
            if (hasPoint)
            {
                _marker.transform.position = _feet + Vector3.up * 0.03f;
                _marker.transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
                if (_markerMaterial != null)
                {
                    _markerMaterial.color = _valid ? _validColor : _invalidColor;
                }
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

            _marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _marker.name = "BagPlacementMarker";
            Collider collider = _marker.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            _marker.transform.localScale = new Vector3(_markerSize, 0.01f, _markerSize);

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
            Renderer renderer = _marker.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = _markerMaterial;
            }

            _marker.SetActive(false);
        }
    }
}
