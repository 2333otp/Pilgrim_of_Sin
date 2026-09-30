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

        [Tooltip("按下時把這個圖示移到同層最前面，放開後還原原本的排序。" +
                 "給「手把示意圖」用（方向鍵四塊圖會互相重疊，反紅圖要完整顯示在最上層）。" +
                 "操作說明「列表」是自動排版，絕對不能開，否則整個列表順序會亂掉。")]
        [SerializeField] private bool _bringToFrontWhenPressed;

        private string _lastScheme;
        private bool _isPressed;
        private InputAction _boundAction;
        private bool _isFront;

        // 按下狀態 = 動作事件 OR 直接讀裝置。只靠動作事件的話，玩家剛從鍵鼠改用手把的第一下，
        // PlayerInput 的 currentControlScheme 還沒切過去，手把綁定被遮罩而吃掉那一下（同 ShouldPause() 的備援理由），
        // 所以多一條直接讀裝置的路徑補上。
        private bool _eventPressed;
        private bool _polledPressed;
        private readonly System.Collections.Generic.List<string> _pollPaths = new System.Collections.Generic.List<string>();
        private const float PollThreshold = 0.35f; // 搖桿／類比扳機的判定門檻

        // 同一個父物件底下可能有多顆同時被按住：記錄原始排序，全部放開後才一次還原，避免順序漂移。
        private class FrontState { public System.Collections.Generic.List<Transform> order; public int count; }
        private static readonly System.Collections.Generic.Dictionary<Transform, FrontState> s_front =
            new System.Collections.Generic.Dictionary<Transform, FrontState>();

        private void SetFront(bool front)
        {
            if (!_bringToFrontWhenPressed || front == _isFront) return;
            Transform parent = transform.parent;
            if (parent == null) return;

            if (front)
            {
                if (!s_front.TryGetValue(parent, out FrontState st))
                {
                    st = new FrontState { order = new System.Collections.Generic.List<Transform>() };
                    foreach (Transform c in parent) st.order.Add(c);
                    s_front[parent] = st;
                }
                st.count++;
                transform.SetAsLastSibling();
                _isFront = true;
            }
            else
            {
                _isFront = false;
                if (s_front.TryGetValue(parent, out FrontState st) && --st.count <= 0)
                {
                    for (int i = 0; i < st.order.Count; i++)
                        if (st.order[i] != null) st.order[i].SetSiblingIndex(i);
                    s_front.Remove(parent);
                }
            }
        }

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
            _eventPressed = false;
            _polledPressed = false;

            SubscribeCurrent();
            Refresh();
        }

        private void OnDisable()
        {
            SetFront(false);
            UnsubscribeCurrent();
        }

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

            _polledPressed = PollPressed();
            ApplyPressed();
        }

        /// <summary>直接檢查這個動作在目前方案下所有綁定的實體按鍵／搖桿是否被按住（任一即算）。</summary>
        private bool PollPressed()
        {
            for (int i = 0; i < _pollPaths.Count; i++)
            {
                using (var controls = InputSystem.FindControls(_pollPaths[i]))
                {
                    foreach (var c in controls)
                    {
                        if (c is UnityEngine.InputSystem.Controls.ButtonControl b ? b.isPressed : c.IsActuated(PollThreshold))
                            return true;
                    }
                }
            }
            return false;
        }

        private void RebuildPollPaths(string scheme)
        {
            _pollPaths.Clear();
            if (_boundAction == null || string.IsNullOrEmpty(scheme)) return;
            foreach (var b in _boundAction.bindings)
            {
                if (b.isComposite || string.IsNullOrEmpty(b.path)) continue;
                if (!string.IsNullOrEmpty(b.groups) && System.Array.IndexOf(b.groups.Split(';'), scheme) < 0) continue;
                _pollPaths.Add(b.path);
            }
        }

        private void ApplyPressed()
        {
            bool pressed = _eventPressed || _polledPressed;
            if (pressed == _isPressed) return;
            _isPressed = pressed;
            SetFront(pressed);
            Refresh();
        }

        private void OnPerformed(InputAction.CallbackContext ctx)
        {
            _eventPressed = true;
            ApplyPressed();
        }

        private void OnCanceled(InputAction.CallbackContext ctx)
        {
            _eventPressed = false;
            ApplyPressed();
        }

        private void Refresh()
        {
            string scheme = !string.IsNullOrEmpty(_forcedScheme)
                ? _forcedScheme
                : InputBindingUtility.GetCurrentScheme(_playerInput);
            RebuildPollPaths(scheme);
            if (_image == null || _boundAction == null || _glyphDatabase == null) return;
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
