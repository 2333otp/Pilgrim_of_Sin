using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PilgrimOfSin
{
    /// <summary>
    /// 操作提示 UI：文字 + 依目前輸入裝置(手把/鍵盤)自動換圖的按鍵圖示。
    /// 圖示格的位置、大小固定（見 Prefab 的 RectTransform + LayoutElement），
    /// 只有裡面的 Sprite 依裝置與 Action 目前實際綁定的按鍵而變動。
    ///
    /// 【使用方式】
    ///   掛在提示 UI 的根物件上，Inspector 指定 _glyphDatabase（專案共用同一份），
    ///   再呼叫 SetData(InputPromptData) 指定要顯示哪一則提示。
    /// </summary>
    public class InputPromptUI : MonoBehaviour
    {
        [Header("資料")]
        [SerializeField] private InputPromptData _data;
        [SerializeField] private InputGlyphDatabase _glyphDatabase;

        [Header("元件參照")]
        [SerializeField] private TMP_Text _text;
        [SerializeField] private Image _iconImage;
        [SerializeField] private PlayerInput _playerInput;

        private string _lastScheme;

        private void OnEnable()
        {
            if (_playerInput == null)
                _playerInput = FindFirstObjectByType<PlayerInput>();

            _lastScheme = null; // 強制下一次 Update/Refresh 重新判斷一次
            Refresh();
        }

        private void Update()
        {
            // 玩家隨時可能從手把切成鍵盤（或反過來），用輪詢比訂閱事件簡單可靠
            string scheme = _playerInput != null ? _playerInput.currentControlScheme : null;
            if (scheme != _lastScheme)
            {
                _lastScheme = scheme;
                Refresh();
            }
        }

        /// <summary>切換這個提示 UI 要顯示的內容。</summary>
        public void SetData(InputPromptData data)
        {
            _data = data;
            Refresh();
        }

        private void Refresh()
        {
            if (_data == null) return;

            if (_text != null) _text.text = _data.PromptText;

            Sprite icon = ResolveIcon();
            if (_iconImage != null)
            {
                _iconImage.sprite = icon;
                _iconImage.enabled = icon != null;
            }
        }

        private Sprite ResolveIcon()
        {
            var action = _data.Action != null ? _data.Action.action : null;
            if (action == null || _glyphDatabase == null) return null;

            string scheme = InputBindingUtility.GetCurrentScheme(_playerInput);
            string path = InputBindingUtility.FindBindingPath(action, scheme);
            return path == null ? null : _glyphDatabase.GetSprite(path);
        }
    }
}
