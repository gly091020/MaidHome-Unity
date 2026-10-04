using System;
using UnityEngine;
using UnityEngine.UI;

namespace MaidHome.Gameplay.Ar
{
    /// <summary>
    /// AR 面板里的一格：一个按钮 + 一个文字（+ 可选的底色图，用来标选中）。
    /// **全部在场景里手摆**，代码不生成；背包里女仆比格子少的时候多余的格子会被关掉。
    /// </summary>
    [Serializable]
    public sealed class MaidArSlot
    {
        [SerializeField] private Button _button;
        [SerializeField] private Text _label;
        [Tooltip("选中时改这张图的颜色，留空就不改")]
        [SerializeField] private Image _background;
        [SerializeField] private Color _normalColor = new Color(1f, 1f, 1f, 0.25f);
        [SerializeField] private Color _selectedColor = new Color(1f, 0.84f, 0.35f, 0.75f);

        public Button Button
        {
            get { return _button; }
        }

        public bool IsUsable
        {
            get { return _button != null; }
        }

        /// <summary>自动生成的那条底栏现搭格子时走这个（普通接线不用管）。</summary>
        public void Bind(Button button, Text label, Image background)
        {
            _button = button;
            _label = label;
            _background = background;
        }

        public void SetVisible(bool visible)
        {
            if (_button != null && _button.gameObject.activeSelf != visible)
            {
                _button.gameObject.SetActive(visible);
            }
        }

        public void Show(string text, bool selected)
        {
            if (_label != null)
            {
                _label.text = text;
            }

            if (_background != null)
            {
                _background.color = selected ? _selectedColor : _normalColor;
            }
        }
    }
}
