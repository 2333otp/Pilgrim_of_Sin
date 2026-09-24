using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PilgrimOfSin
{
    /// <summary>
    /// 單一按鍵圖示：依目前裝置(手把/鍵盤)從 InputGlyphDatabase 查表顯示對應圖示，
    /// 並在對應 Action 被按下的當下，切換成高亮版（金色→桃紅色），放開恢復。
    ///
    /// 只在物件啟用(OnEnable)時才訂閱輸入事件、停用(OnDisable)時取消訂閱，
    /// 所以掛在「操作說明」面板底下時，面板關閉即自動停止監聽，不會在一般遊戲中持續耗費效能。
    ///
    /// 用途：「操作說明」總覽列表的每一行圖示、手把示意圖上的每一顆按鍵，共用這個元件。
    /// </summary>
    public class GlyphHighlightIcon : MonoBehaviour
    {
        [Header("資料")]
        [SerializeField] private InputActionReference _action;
        [SerializeField] private InputGlyphDatabase _glyphDatabase;

        [Header("元件參照")]
        [SerializeField] private Image _image;
        [SerializeField] private PlayerInput _playerInput;

        [Tooltip("留空 = 跟隨玩家目前實際使用的裝置自動切換（給操作說明列表這種要同步顯示鍵盤/手把的地方用）。" +
                 "填 \"Gamepad\" 或 \"Keyboard&Mouse\" = 固定顯示該裝置版本，不受玩家目前用什麼裝置影響" +
                 "（給「手把示意圖」這種本身就固定畫的是某一種裝置外觀的整體圖用，不會因為玩家換鍵盤操作就變成別的東西）。")]
        [SerializeField] private string _forcedScheme;

        private string _lastScheme;
        private bool _isPressed;
        private InputAction _boundAction;

        /// <summary>動態指定要監聽的 Action 與圖示資料庫（給動態生成的清單行使用；靜態擺放在手把示意圖上的實例則直接在 Inspector 指定即可，不需要呼叫這個）。</summary>
        public void Setup(InputActionReference action, InputGlyphDatabase glyphDatabase)
        {
            UnsubscribeCurrent();

            _action = action;
            _glyphDatabase = glyphDatabase;

            if (isActiveAndEnabled)
            {
                SubscribeCurrent();
                Refresh();
            }
        }

        private void OnEnable()
        {
            if (_playerInput == null)
                _playerInput = FindFirstObjectByType<PlayerInput>();

            _lastScheme = null;
            _isPressed = false;

            SubscribeCurrent();
            Refresh();
        }

        private void OnDisable() => UnsubscribeCurrent();

        private void SubscribeCurrent()
        {
            _boundAction = _action != null ? _action.action : null;
            if (_boundAction != null)
            {
                _boundAction.performed += OnPerformed;
                _boundAction.canceled += OnCanceled;
            }
        }

        private void UnsubscribeCurrent()
        {
            if (_boundAction != null)
            {
                _boundAction.performed -= OnPerformed;
                _boundAction.canceled -= OnCanceled;
                _boundAction = null;
            }
        }

        private void Update()
        {
            string scheme = _playerInput != null ? _playerInput.currentControlScheme : null;
            if (scheme != _lastScheme)
            {
                _lastScheme = scheme;
                Refresh();
            }
        }

        private void OnPerformed(InputAction.CallbackContext ctx)
        {
            _isPressed = true;
            Refresh();
        }

        private void OnCanceled(InputAction.CallbackContext ctx)
        {
            _isPressed = false;
            Refresh();
        }

        private void Refresh()
        {
            if (_image == null || _boundAction == null || _glyphDatabase == null) return;

            string scheme = !string.IsNullOrEmpty(_forcedScheme)
                ? _forcedScheme
                : InputBindingUtility.GetCurrentScheme(_playerInput);
            string path = InputBindingUtility.FindBindingPath(_boundAction, scheme);
            if (path == null)
            {
                _image.enabled = false;
                return;
            }

            Sprite sprite = _isPressed ? _glyphDatabase.GetPressedSprite(path) : _glyphDatabase.GetSprite(path);
            _image.sprite = sprite;
            _image.enabled = sprite != null;
        }
    }
}
