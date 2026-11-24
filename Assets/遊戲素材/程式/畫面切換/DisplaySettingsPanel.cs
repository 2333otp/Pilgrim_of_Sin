using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using PilgrimOfSin.StateMachine;

namespace PilgrimOfSin
{
    /// <summary>
    /// 設置選單「畫面與震動」子頁：鏡頭晃動強度滑桿＋手把震動開關。
    /// 放在共用的 SettingsSubPanels.prefab 裡，主選單（MainMenuSettingsPanel）和 ESC 選單（PauseMenuUI）共用。
    ///
    /// 這一頁自己處理所有輸入（上下選項目、左右調整、滑鼠點擊）與存檔，
    /// 兩邊選單只負責「打開／關掉這一頁」和返回鍵，邏輯只有這一份，不用兩邊各寫一次。
    /// 輸入一律 New Input System；暫停中（timeScale = 0）也能操作（全部用真實時間）。
    /// </summary>
    public class DisplaySettingsPanel : MonoBehaviour
    {
        [Header("鏡頭晃動")]
        [SerializeField] private Slider _shakeSlider;
        [Tooltip("顯示目前百分比（例如 70%）。")]
        [SerializeField] private Text _shakeValueText;

        [Header("手把震動")]
        [Tooltip("顯示「開啟／關閉」，滑鼠點這裡切換。")]
        [SerializeField] private Text _rumbleValueText;

        [Header("反白")]
        [SerializeField] private Color _rumbleNormalColor = Color.white;
        [SerializeField] private Color _rumbleHighlightColor = new Color(1f, 0.82f, 0.35f, 1f);

        private const float Step = 0.1f;            // 左右鍵一次調 10%
        private const float SelectedScale = 1.08f;  // 跟其他選單反白一樣
        private const float NavRepeatDelay = 0.18f;

        private PlayerInputReader _inputReader;
        private int _index;                         // 0 = 鏡頭晃動、1 = 手把震動
        private float _navCooldown;
        private Vector2? _lastMousePos;
        private bool _dirty;

        private void Awake()
        {
            if (_shakeSlider != null)
            {
                _shakeSlider.minValue = 0f;
                _shakeSlider.maxValue = 1f;
                _shakeSlider.onValueChanged.AddListener(OnShakeChanged);
            }
        }

        private void OnDestroy()
        {
            if (_shakeSlider != null) _shakeSlider.onValueChanged.RemoveListener(OnShakeChanged);
        }

        private void OnEnable()
        {
            _inputReader = FindFirstObjectByType<PlayerInputReader>();
            _shakeSlider?.SetValueWithoutNotify(HitFeedbackPrefs.ShakeIntensity);
            RefreshTexts();
            _index = 0;
            _navCooldown = NavRepeatDelay;   // 剛從上一頁按確認進來，避免同一下被當成這一頁的操作
            _lastMousePos = null;
            _dirty = false;
            RefreshHighlight();
        }

        private void OnDisable()
        {
            // 離開這一頁才寫入磁碟（拖滑桿時不用每一格都寫）
            if (_dirty) HitFeedbackPrefs.Save();
            _dirty = false;
            SetHighlight(0, false);
            SetHighlight(1, false);
        }

        private void Update()
        {
            if (_navCooldown > 0f) _navCooldown -= Time.unscaledDeltaTime;

            HandleMouse();
            HandleUpDown();
            HandleLeftRight();
        }

        // ── 輸入 ──────────────────────────────────────────────────────

        private void HandleUpDown()
        {
            if (_navCooldown > 0f) return;

            var pad = Gamepad.current;
            var kb = Keyboard.current;
            float stickY = pad != null ? pad.leftStick.ReadValue().y : 0f;
            bool up = (_inputReader != null && _inputReader.MenuUpPressed)
                      || (pad != null && (pad.dpad.up.isPressed || stickY > 0.5f))
                      || (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame));
            bool down = (_inputReader != null && _inputReader.MenuDownPressed)
                        || (pad != null && (pad.dpad.down.isPressed || stickY < -0.5f))
                        || (kb != null && (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame));

            if (up == down) return;
            Select(_index == 0 ? 1 : 0);   // 只有兩項，上下都是切換
            _navCooldown = NavRepeatDelay;
        }

        private void HandleLeftRight()
        {
            var pad = Gamepad.current;
            var kb = Keyboard.current;
            // 直讀裝置備援：同音量頁，避免 currentControlScheme 還沒切過去時吃掉第一下
            bool right = (_inputReader != null && _inputReader.VolumeUpPressed)
                         || (pad != null && (pad.dpad.right.wasPressedThisFrame || pad.leftStick.right.wasPressedThisFrame))
                         || (kb != null && (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame));
            bool left = (_inputReader != null && _inputReader.VolumeDownPressed)
                        || (pad != null && (pad.dpad.left.wasPressedThisFrame || pad.leftStick.left.wasPressedThisFrame))
                        || (kb != null && (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame));
            if (right == left) return;

            if (_index == 0)
            {
                if (_shakeSlider != null)
                    _shakeSlider.value = Mathf.Clamp01(Mathf.Round((_shakeSlider.value + (right ? Step : -Step)) * 10f) / 10f);
            }
            else ToggleRumble();
        }

        private void HandleMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 pos = mouse.position.ReadValue();
            // 滑鼠真的有動才改反白（避免蓋掉手把剛選的項目）；剛打開那一幀只記座標
            if (_lastMousePos == null) _lastMousePos = pos;
            else if (pos != _lastMousePos.Value)
            {
                _lastMousePos = pos;
                if (IsOver(_shakeSlider != null ? _shakeSlider.transform.parent as RectTransform : null, pos)) Select(0);
                else if (IsOver(_rumbleValueText != null ? _rumbleValueText.rectTransform : null, pos)) Select(1);
            }

            // 開關用手動點擊判定（滑桿拖拉交給 EventSystem）
            if (mouse.leftButton.wasPressedThisFrame && IsOver(_rumbleValueText != null ? _rumbleValueText.rectTransform : null, pos))
            {
                Select(1);
                ToggleRumble();
            }
        }

        private static bool IsOver(RectTransform rect, Vector2 screenPos)
        {
            if (rect == null || !rect.gameObject.activeInHierarchy) return false;
            var canvas = rect.GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(rect, screenPos, cam);
        }

        // ── 值 ────────────────────────────────────────────────────────

        private void OnShakeChanged(float value)
        {
            HitFeedbackPrefs.ShakeIntensity = value;
            HitFeedback.RefreshPrefs();
            _dirty = true;
            RefreshTexts();
        }

        private void ToggleRumble()
        {
            bool on = !HitFeedbackPrefs.RumbleEnabled;
            HitFeedbackPrefs.RumbleEnabled = on;
            HitFeedback.RefreshPrefs();
            _dirty = true;
            RefreshTexts();
            if (on) HitFeedback.PreviewRumble();   // 打開時試震一下，讓玩家知道效果
        }

        private void RefreshTexts()
        {
            if (_shakeValueText != null)
                _shakeValueText.text = $"{Mathf.RoundToInt(HitFeedbackPrefs.ShakeIntensity * 100f)}%";
            if (_rumbleValueText != null)
                _rumbleValueText.text = HitFeedbackPrefs.RumbleEnabled ? "＜  開啟  ＞" : "＜  關閉  ＞";
        }

        // ── 反白 ──────────────────────────────────────────────────────

        private void Select(int index)
        {
            if (index == _index) return;
            _index = index;
            RefreshHighlight();
        }

        private void RefreshHighlight()
        {
            SetHighlight(0, _index == 0);
            SetHighlight(1, _index == 1);
        }

        private void SetHighlight(int index, bool on)
        {
            if (index == 0)
            {
                if (_shakeSlider == null) return;
                var g = _shakeSlider.targetGraphic;
                if (g != null) g.color = on ? _shakeSlider.colors.highlightedColor : _shakeSlider.colors.normalColor;
                _shakeSlider.transform.localScale = on ? Vector3.one * SelectedScale : Vector3.one;
            }
            else if (_rumbleValueText != null)
            {
                _rumbleValueText.color = on ? _rumbleHighlightColor : _rumbleNormalColor;
                _rumbleValueText.transform.localScale = on ? Vector3.one * SelectedScale : Vector3.one;
            }
        }
    }
}
