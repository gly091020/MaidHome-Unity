using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MaidHome.Core.Input
{
    /// <summary>
    /// 全工程唯一的指针输入入口：只认触摸，PC 和 Android 走同一条路。
    /// 交互代码不许再直接读 Input，编辑器里靠 Device Simulator 的 Touch 模式模拟。
    /// </summary>
    public static class PointerInput
    {
        /// <summary>一帧的指针状态，同一帧里所有调用者看到的是同一份。</summary>
        public struct Pointer
        {
            /// <summary>本帧按下。</summary>
            public bool Pressed;

            /// <summary>手指还按着（含按下那一帧）。</summary>
            public bool Held;

            /// <summary>本帧抬起；手指被系统吞掉也补一个抬起，免得拖拽卡死。</summary>
            public bool Released;

            /// <summary>这段手势是不是从 UI 上开始的。按下那一刻定死，中途划到 UI 上不算。</summary>
            public bool OverUi;

            public int FingerId;
            public Vector2 Position;
        }

        static readonly List<RaycastResult> UiHits = new List<RaycastResult>();

        static readonly List<Pointer> Pointers = new List<Pointer>();
        static readonly List<Pointer> Previous = new List<Pointer>();
        static int _cachedFrame = -1;
        static EventSystem _raycastSystem;
        static PointerEventData _raycastData;

        /// <summary>
        /// 当前主指针（先按下的那根手指）。没有触摸时三个状态位全是 false。
        /// 需要两根手指同时用（例如同时拽两只耳朵）就调 CopyPointers 拿全部。
        /// </summary>
        public static Pointer Primary
        {
            get
            {
                Refresh();
                return Pointers.Count > 0 ? Pointers[0] : new Pointer();
            }
        }

        /// <summary>
        /// 把这一帧按住的所有手指拷进 into（顺序稳定：先按下的排前面，所以 [0] 就是 Primary）。
        /// 同一根手指整段手势里 OverUi 都是按下那一刻的值；抬起那一帧还会在列表里，带 Released。
        /// </summary>
        public static void CopyPointers(List<Pointer> into)
        {
            Refresh();
            if (into == null)
            {
                return;
            }

            into.Clear();
            for (int i = 0; i < Pointers.Count; i++)
            {
                into.Add(Pointers[i]);
            }
        }

        /// <summary>Android 返回键和电脑 Esc 都是这个键码；放这里是为了全工程只有一处读 Input。</summary>
        public static bool BackPressed
        {
            get { return UnityEngine.Input.GetKeyDown(KeyCode.Escape); }
        }

        /// <summary>调试用：F8。同样只在这里读 Input，业务脚本不要自己调 Input。</summary>
        public static bool DebugTogglePressed
        {
            get { return UnityEngine.Input.GetKeyDown(KeyCode.F8); }
        }

        /// <summary>
        /// PC / 编辑器：鼠标滚轮这一帧滚了多少（往上滚是正）。触摸设备用捏合，不走这里。
        /// </summary>
        public static float ScrollDelta
        {
            get { return UnityEngine.Input.mouseScrollDelta.y; }
        }

        /// <summary>PC / 编辑器：鼠标位置（滚轮缩放的锚点）。触摸设备用不到。</summary>
        public static Vector2 MousePosition
        {
            get { return UnityEngine.Input.mousePosition; }
        }

        /// <summary>
        /// 屏幕点现在压在 UI 上吗。自己拿坐标来问用这个——EventSystem 那份缓存要等它的 Update 跑完，
        /// 和我们的 Update 谁先谁后不确定，按下那一帧会误判。
        /// </summary>
        public static bool IsOverUi(Vector2 screenPosition)
        {
            EventSystem system = EventSystem.current;
            if (system == null)
            {
                return false;
            }

            if (_raycastData == null || _raycastSystem != system)
            {
                _raycastSystem = system;
                _raycastData = new PointerEventData(system);
            }

            _raycastData.position = screenPosition;
            UiHits.Clear();
            system.RaycastAll(_raycastData, UiHits);
            return UiHits.Count > 0;
        }

        /// <summary>一帧只算一次：把 Input 的触摸整理成按"按下顺序"排好的列表。</summary>
        static void Refresh()
        {
            if (_cachedFrame == Time.frameCount)
            {
                return;
            }

            _cachedFrame = Time.frameCount;

            Previous.Clear();
            for (int i = 0; i < Pointers.Count; i++)
            {
                Previous.Add(Pointers[i]);
            }

            Pointers.Clear();

            int count = UnityEngine.Input.touchCount;

            // 老手指按原顺序更新，顺序稳定，Primary 永远是先按下的那根
            for (int i = 0; i < Previous.Count; i++)
            {
                int index = FindTouch(Previous[i].FingerId, count);
                if (index < 0)
                {
                    continue; // 系统吞了，下面补一个 Released
                }

                Pointers.Add(Read(UnityEngine.Input.GetTouch(index), Previous[i], false));
            }

            for (int i = 0; i < count; i++)
            {
                Touch touch = UnityEngine.Input.GetTouch(i);
                if (Contains(touch.fingerId))
                {
                    continue;
                }

                // 一根手指在同一帧里按下又抬起（快速点）也要报出来，否则点一下就没了
                Pointers.Add(Read(touch, new Pointer(), true));
            }

            for (int i = 0; i < Previous.Count; i++)
            {
                if (!Previous[i].Held || Contains(Previous[i].FingerId))
                {
                    continue;
                }

                Pointer lost = Previous[i];
                lost.Pressed = false;
                lost.Held = false;
                lost.Released = true;
                Pointers.Add(lost);
            }

#if UNITY_EDITOR
            if (count == 0)
            {
                HintEditorTouchMode();
            }
#endif
        }

        static Pointer Read(Touch touch, Pointer previous, bool isNew)
        {
            Pointer pointer = new Pointer();
            pointer.FingerId = touch.fingerId;
            pointer.Position = touch.position;
            if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
            {
                pointer.Released = true;
                if (isNew)
                {
                    pointer.Pressed = true;
                    pointer.OverUi = IsOverUi(touch.position);
                }
                else
                {
                    pointer.OverUi = previous.OverUi;
                }

                return pointer;
            }

            pointer.Held = true;
            if (isNew)
            {
                // 新手指：OverUi 在按下这一刻定死
                pointer.Pressed = touch.phase == TouchPhase.Began;
                pointer.OverUi = IsOverUi(touch.position);
            }
            else
            {
                pointer.OverUi = previous.OverUi;
            }

            return pointer;
        }

        static bool Contains(int fingerId)
        {
            for (int i = 0; i < Pointers.Count; i++)
            {
                if (Pointers[i].FingerId == fingerId)
                {
                    return true;
                }
            }

            return false;
        }

        static int FindTouch(int fingerId, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (UnityEngine.Input.GetTouch(i).fingerId == fingerId)
                {
                    return i;
                }
            }

            return -1;
        }

#if UNITY_EDITOR
        static bool _hintedEditorTouchMode;

        // 编辑器里忘了开 Device Simulator 的 Touch 模式时，鼠标怎么点都没反应，给一条提示免得当成 bug
        static void HintEditorTouchMode()
        {
            if (_hintedEditorTouchMode || !UnityEngine.Input.GetMouseButtonDown(0))
            {
                return;
            }

            _hintedEditorTouchMode = true;
            Debug.LogWarning("交互只认触摸：编辑器里请到 Window > General > Device Simulator 打开 Touch 模式。");
        }
#endif
    }
}
