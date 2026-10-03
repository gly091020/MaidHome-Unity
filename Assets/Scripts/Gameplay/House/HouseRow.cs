using System;
using MaidHome.Interop.House;
using UnityEngine;
using UnityEngine.UI;

namespace MaidHome.Gameplay.House
{
    /// <summary>
    /// 房子列表里的一行。挂在自己的行预制体上，由 HousePanel 调 Bind。
    /// 场景里摆的样式就是最终样式，这里只改文字/颜色/能不能点。
    ///
    /// 「删除」按钮：预制体上接了就用你那个，不接就在行右侧自动生成一个；删除是**点两下确认**
    /// （第一下变成"再点一次"，_confirmSeconds 秒内再点才真删），不用另做确认弹窗。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HouseRow : MonoBehaviour
    {
        [SerializeField] private Image _background;
        [Tooltip("房子名，留空就不改文字")]
        [SerializeField] private Text _label;
        [Tooltip("整行做成按钮用的，点了就切这栋")]
        [SerializeField] private Button _button;
        [Tooltip("按钮上的小字，可选")]
        [SerializeField] private Text _buttonLabel;
        [Tooltip("删除按钮，留空就在行右侧自动生成一个")]
        [SerializeField] private Button _deleteButton;
        [Tooltip("删除按钮上的字，留空自动生成时自己建")]
        [SerializeField] private Text _deleteLabel;
        [Tooltip("点第一下之后多久内点第二下才认（秒）")]
        [SerializeField] private float _confirmSeconds = 3f;

        [SerializeField] private Color _normalColor = new Color(0.16f, 0.16f, 0.2f, 0.9f);
        [SerializeField] private Color _currentColor = new Color(0.1f, 0.32f, 0.2f, 0.9f);
        [SerializeField] private Color _lockedColor = new Color(0.12f, 0.12f, 0.15f, 0.55f);

        Action _onDelete;
        float _armedAt = -100f;

        const string DeleteText = "删除";
        const string ConfirmText = "再点一次";

        void Awake()
        {
            EnsureDeleteButton();
        }

        /// <summary>代码生成的行用它把自己的几个部件交进来（预制体行不用调）</summary>
        public void BindGenerated(Image background, Text label, Button button, Text buttonLabel)
        {
            _background = background;
            _label = label;
            _button = button;
            _buttonLabel = buttonLabel;
            EnsureDeleteButton();
        }

        public void Bind(HouseSaveData house, bool isCurrent, bool locked, Action onSwitch, Action onDelete)
        {
            // 行可能在"父节点还没激活"的时候就被 Bind（Awake 还没跑），这里先保证删除按钮存在
            EnsureDeleteButton();
            _onDelete = onDelete;
            _armedAt = -100f;
            ResetDeleteLabel();

            if (_label != null)
            {
                _label.text = HouseSwitcher.DisplayName(house);
            }

            if (_background != null)
            {
                _background.color = isCurrent ? _currentColor : locked ? _lockedColor : _normalColor;
            }

            if (_buttonLabel != null)
            {
                _buttonLabel.text = isCurrent ? "当前" : locked ? "…" : "切换";
            }

            if (_deleteButton != null)
            {
                // 当前这栋不能删（先切走），切换过程中也先别动
                _deleteButton.gameObject.SetActive(!isCurrent);
                _deleteButton.interactable = !locked && onDelete != null;
                _deleteButton.onClick.RemoveListener(OnDeleteClicked);
                _deleteButton.onClick.AddListener(OnDeleteClicked);
            }

            if (_button == null)
            {
                return;
            }

            _button.onClick.RemoveAllListeners();
            _button.interactable = !isCurrent && !locked;
            if (onSwitch != null)
            {
                _button.onClick.AddListener(() => onSwitch());
            }
        }

        void OnDestroy()
        {
            if (_deleteButton != null)
            {
                _deleteButton.onClick.RemoveListener(OnDeleteClicked);
            }
        }

        /// <summary>第一下只是"上膛"，第二下才真删——移动端没有确认弹窗，这样最省事</summary>
        void OnDeleteClicked()
        {
            if (_onDelete == null)
            {
                return;
            }

            if (Time.unscaledTime - _armedAt > Mathf.Max(0.2f, _confirmSeconds))
            {
                _armedAt = Time.unscaledTime;
                if (_deleteLabel != null)
                {
                    _deleteLabel.text = ConfirmText;
                }

                return;
            }

            _armedAt = -100f;
            Action callback = _onDelete;
            _onDelete = null;
            callback();
        }

        void ResetDeleteLabel()
        {
            if (_deleteLabel != null)
            {
                _deleteLabel.text = DeleteText;
            }
        }

        /// <summary>预制体上没接删除按钮就自己搭一个（行右侧，红底白字）</summary>
        void EnsureDeleteButton()
        {
            if (_deleteButton != null || !(transform is RectTransform row))
            {
                return;
            }

            GameObject buttonObject = new GameObject("Delete", typeof(RectTransform), typeof(Image),
                typeof(Button));
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.SetParent(row, false);
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(104f, 46f);
            rect.anchoredPosition = new Vector2(-10f, 0f);
            buttonObject.GetComponent<Image>().color = new Color(0.62f, 0.18f, 0.18f, 0.95f);

            _deleteButton = buttonObject.GetComponent<Button>();
            _deleteLabel = CreateText(rect, DeleteText);
        }

        static Text CreateText(RectTransform parent, string content)
        {
            GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            Text text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 20;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = content;

            RectTransform rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return text;
        }
    }
}
