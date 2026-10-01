using System;
using MaidHome.Gameplay.House;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 点场景里的女仆：暂停游走、相机平滑拉近、打开交互面板。
    /// 点空白或面板关闭按钮取消，女仆恢复游走，相机回房子视角。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaidInteractionController : MonoBehaviour
    {
        [SerializeField] private MaidInteractionPanel _panel;
        [SerializeField] private HouseCameraFitter _cameraFitter;
        [Tooltip("moreanimation 的 tail_sniff.ogg，留空就只是没声音")]
        [SerializeField] private AudioClip _tailSniffClip;
        [SerializeField] private float _clickDistance = 300f;
        [SerializeField] private LayerMask _clickMask = ~0;

        MaidAgent _current;
        MaidTailInteraction _tail;

        void Awake()
        {
            if (_panel == null)
            {
                _panel = GetComponent<MaidInteractionPanel>();
            }

            if (_panel == null)
            {
                _panel = gameObject.AddComponent<MaidInteractionPanel>();
            }

            if (_cameraFitter == null && Camera.main != null)
            {
                _cameraFitter = Camera.main.GetComponent<HouseCameraFitter>();
            }

            _tail = GetComponent<MaidTailInteraction>();
            if (_tail == null)
            {
                _tail = gameObject.AddComponent<MaidTailInteraction>();
            }
        }

        void OnEnable()
        {
            if (_panel != null)
            {
                _panel.CloseRequested += Close;
                _panel.TailRequested += OpenTail;
            }

            if (_tail != null)
            {
                _tail.Ended += OnTailEnded;
            }
        }

        void OnDisable()
        {
            if (_panel != null)
            {
                _panel.CloseRequested -= Close;
                _panel.TailRequested -= OpenTail;
            }

            if (_tail != null)
            {
                _tail.Ended -= OnTailEnded;
            }
        }

        void Update()
        {
            if (_current != null && !_current.gameObject.activeInHierarchy)
            {
                Close();
                return;
            }

            // 摸尾巴模式自己处理输入，这里别抢
            if (_tail != null && _tail.IsActive)
            {
                return;
            }

            if (_current == null && _panel != null && _panel.IsOpen)
            {
                Close();
            }

            if (!PointerPressed() || IsPointerOverUi())
            {
                return;
            }

            MaidAgent agent = RaycastMaid();
            if (agent != null)
            {
                Open(agent);
                return;
            }

            if (_current != null)
            {
                Close();
            }
        }

        void Open(MaidAgent agent)
        {
            if (agent == null)
            {
                return;
            }

            if (_current == agent && _panel != null && _panel.IsOpen)
            {
                return;
            }

            if (_current != null && _current != agent)
            {
                SetPaused(_current, false);
            }

            _current = agent;
            SetPaused(_current, true);

            if (_cameraFitter != null)
            {
                _cameraFitter.FocusOn(_current.GetBounds(), _current.transform);
            }

            if (_panel != null)
            {
                _panel.Open(_current.Save, _tail != null && _tail.Supports(_current));
            }
        }

        void Close()
        {
            if (_tail != null && _tail.IsActive)
            {
                _tail.Abort();
            }

            if (_current != null && _current.gameObject.activeInHierarchy)
            {
                SetPaused(_current, false);
            }

            _current = null;
            if (_cameraFitter != null)
            {
                _cameraFitter.RestoreHouseView();
            }

            if (_panel != null)
            {
                _panel.Close();
            }
        }

        void OpenTail()
        {
            if (_tail == null || _current == null || _tail.IsActive)
            {
                return;
            }

            if (_tail.Begin(_current, _tailSniffClip))
            {
                _panel.Close();
            }
        }

        void OnTailEnded()
        {
            if (_current != null && _panel != null)
            {
                _panel.Open(_current.Save, _tail != null && _tail.Supports(_current));
            }
        }

        static void SetPaused(MaidAgent agent, bool paused)
        {
            if (agent == null)
            {
                return;
            }

            MaidWanderer wanderer = agent.Wanderer;
            if (wanderer != null)
            {
                wanderer.SetPaused(paused);
            }
        }

        MaidAgent RaycastMaid()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return null;
            }

            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            RaycastHit[] hits = Physics.RaycastAll(ray, _clickDistance, _clickMask);
            Array.Sort(hits, CompareHits);
            for (int i = 0; i < hits.Length; i++)
            {
                MaidAgent agent = hits[i].collider.GetComponentInParent<MaidAgent>();
                if (agent != null)
                {
                    return agent;
                }

                // 命中墙/地板就先当被挡住，避免隔着墙点到女仆
                if (hits[i].collider.GetComponentInParent<HouseGridView>() != null)
                {
                    return null;
                }
            }

            return null;
        }

        static int CompareHits(RaycastHit a, RaycastHit b)
        {
            return a.distance.CompareTo(b.distance);
        }

        static bool PointerPressed()
        {
            if (Input.GetMouseButtonDown(0))
            {
                return true;
            }

            return Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began;
        }

        static bool IsPointerOverUi()
        {
            if (EventSystem.current == null)
            {
                return false;
            }

            if (Input.touchCount > 0)
            {
                return EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId);
            }

            return EventSystem.current.IsPointerOverGameObject();
        }
    }
}
