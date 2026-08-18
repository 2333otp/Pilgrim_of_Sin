using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace PilgrimOfSin
{
    /// <summary>
    /// 主選單「設置選單」面板控制。
    /// 面板美術與音量綁定邏輯沿用 ESC 選單（PauseMenuUI）的設定子面板。
    /// 子面板間的返回改用 ESC 鍵（New Input System），不使用返回按鈕。
    ///
    /// 手把/滑鼠反白統一手動控制（比照 PauseMenuUI／MainMenuUI），
    /// 不使用 Unity 內建 EventSystem 的 Selected／Highlighted 自動切換。
    /// </summary>
    public class MainMenuSettingsPanel : MonoBehaviour
    {
        [Header("開啟入口")]
        [SerializeField] private Button _settingsButton;
        [SerializeField] private GameObject _mainButtonGroup;

        [Header("子面板根物件")]
        [SerializeField] private GameObject _settingsSubPanel;
        [SerializeField] private GameObject _volumeSubPanel;
        [SerializeField] private GameObject _controlsSubPanel;
        [SerializeField] private GameObject _creditsSubPanel;

        [Header("設置選單 - 入口按鈕")]
        [SerializeField] private Button _btnSettingsVolume;
        [SerializeField] private Button _btnSettingsControls;
        [SerializeField] private Button _btnSettingsCredits;

        [Header("音量滑桿")]
        [SerializeField] private Slider _masterSlider;
        [SerializeField] private Slider _musicSlider;
        [SerializeField] private Slider _sfxSlider;

        private const float SelectedButtonScale = 1.08f;
        private const float NavRepeatDelay = 0.18f;

        // 手把導航：設置選單 3 顆入口按鈕
        private Button[] _navButtons;
        private int _navIndex = -1;
        private Button _hoveredButton;

        // 手把導航：音量子面板的滑桿（滑桿不是按鈕，不能共用 _navButtons）
        private Slider[] _volumeSliders;
        private int _volumeSliderIndex;
        private Slider _hoveredVolumeSlider;

        private Vector2? _lastMousePos;
        private float _navCooldown;

        private void Start()
        {
            _settingsButton.onClick.AddListener(OpenSettings);
            _btnSettingsVolume.onClick.AddListener(() => Show(_volumeSubPanel));
            _btnSettingsControls.onClick.AddListener(() => Show(_controlsSubPanel));
            _btnSettingsCredits.onClick.AddListener(() => Show(_creditsSubPanel));

            BindSliders();
            HideAll();
        }

        // ── ESC 鍵／手把返回鍵（南鍵、R2）返回（比照 ESC 暫停選單邏輯）─────

        private void Update()
        {
            if (!_settingsSubPanel.activeSelf && !_volumeSubPanel.activeSelf &&
                !_controlsSubPanel.activeSelf && !_creditsSubPanel.activeSelf)
                return;

            if (_navCooldown > 0f) _navCooldown -= Time.unscaledDeltaTime;

            bool keyboardBack = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            bool gamepadBack  = Gamepad.current  != null &&
                                 (Gamepad.current.buttonSouth.wasPressedThisFrame ||
                                  Gamepad.current.rightTrigger.wasPressedThisFrame);

            if (_volumeSubPanel.activeSelf)
                HandleVolumeSubPanelNav();
            else if (_settingsSubPanel.activeSelf)
                HandleSettingsSubPanelNav();

            if (!keyboardBack && !gamepadBack)
                return;

            if (_volumeSubPanel.activeSelf || _controlsSubPanel.activeSelf || _creditsSubPanel.activeSelf)
            {
                Show(_settingsSubPanel);
            }
            else if (_settingsSubPanel.activeSelf)
            {
                CloseSettings();
            }
        }

        // ── 設置選單子面板：3 顆入口按鈕的手把/滑鼠導航 ─────────────────

        private void HandleSettingsSubPanelNav()
        {
            if (Mouse.current != null)
            {
                Vector2 pos = Mouse.current.position.ReadValue();
                if (_lastMousePos == null)
                {
                    _lastMousePos = pos;
                }
                else if (pos != _lastMousePos.Value)
                {
                    _lastMousePos = pos;
                    SetHovered(GetHoveredButton(pos));
                }
            }

            if (Gamepad.current == null || _navButtons == null || _navButtons.Length == 0) return;

            if (_navCooldown <= 0f)
            {
                float stickY = Gamepad.current.leftStick.ReadValue().y;
                bool up   = Gamepad.current.dpad.up.isPressed   || stickY > 0.5f;
                bool down = Gamepad.current.dpad.down.isPressed || stickY < -0.5f;

                if (up)   { Navigate(-1); _navCooldown = NavRepeatDelay; }
                else if (down) { Navigate(1); _navCooldown = NavRepeatDelay; }
            }

            if (Gamepad.current.buttonEast.wasPressedThisFrame &&
                _navIndex >= 0 && _navIndex < _navButtons.Length)
            {
                var btn = _navButtons[_navIndex];
                if (btn != null && btn.gameObject.activeInHierarchy && btn.interactable)
                    btn.onClick.Invoke();
            }
        }

        private void Navigate(int dir)
        {
            if (_navButtons == null || _navButtons.Length == 0) return;

            bool isFresh = _navIndex < 0;
            int idx = isFresh ? (dir > 0 ? 0 : _navButtons.Length - 1) : _navIndex;

            for (int i = 0; i < _navButtons.Length; i++)
            {
                if (!(isFresh && i == 0))
                    idx = (idx + dir + _navButtons.Length) % _navButtons.Length;

                var btn = _navButtons[idx];
                if (btn != null && btn.gameObject.activeInHierarchy && btn.interactable)
                {
                    SetHovered(btn);
                    return;
                }
            }
        }

        private void SetHovered(Button btn)
        {
            if (btn == _hoveredButton) return;
            ApplyColor(_hoveredButton, false);
            _hoveredButton = btn;
            ApplyColor(_hoveredButton, true);
            _navIndex = (_navButtons != null && btn != null) ? System.Array.IndexOf(_navButtons, btn) : -1;
        }

        private Button GetHoveredButton(Vector2 pos)
        {
            if (_navButtons == null) return null;
            foreach (var btn in _navButtons)
                if (IsOver(btn, pos)) return btn;
            return null;
        }

        private bool IsOver(Button btn, Vector2 screenPos)
        {
            if (btn == null || !btn.gameObject.activeInHierarchy || !btn.interactable) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(
                btn.GetComponent<RectTransform>(), screenPos, null);
        }

        // 參數用 Selectable，這樣滑桿也能共用同一套反白/縮放邏輯
        private void ApplyColor(Selectable sel, bool highlighted)
        {
            if (sel == null || sel.targetGraphic == null) return;
            var colors = sel.colors;
            Color target = highlighted ? colors.highlightedColor : colors.normalColor;
            sel.targetGraphic.color = target;
            sel.transform.localScale = highlighted ? Vector3.one * SelectedButtonScale : Vector3.one;
        }

        // ── 音量子面板：上下切換滑桿、左右調整目前選到的那條 ────────────

        private void HandleVolumeSubPanelNav()
        {
            if (_volumeSliders == null)
                _volumeSliders = new[] { _masterSlider, _musicSlider, _sfxSlider };

            if (Gamepad.current == null) return;

            if (_navCooldown <= 0f)
            {
                float stickY = Gamepad.current.leftStick.ReadValue().y;
                bool up   = Gamepad.current.dpad.up.isPressed   || stickY > 0.5f;
                bool down = Gamepad.current.dpad.down.isPressed || stickY < -0.5f;

                if (up)   { MoveVolumeSelection(-1); _navCooldown = NavRepeatDelay; }
                else if (down) { MoveVolumeSelection(1); _navCooldown = NavRepeatDelay; }
            }

            if (_volumeSliders.Length == 0) return;
            int idx = Mathf.Clamp(_volumeSliderIndex, 0, _volumeSliders.Length - 1);
            Slider current = _volumeSliders[idx];
            if (Gamepad.current.dpad.right.wasPressedThisFrame)
                AdjustSlider(current, 0.05f);
            if (Gamepad.current.dpad.left.wasPressedThisFrame)
                AdjustSlider(current, -0.05f);
        }

        private void MoveVolumeSelection(int dir)
        {
            if (_volumeSliders == null || _volumeSliders.Length == 0) return;
            ApplyColor(_hoveredVolumeSlider, false);
            _volumeSliderIndex   = (_volumeSliderIndex + dir + _volumeSliders.Length) % _volumeSliders.Length;
            _hoveredVolumeSlider = _volumeSliders[_volumeSliderIndex];
            ApplyColor(_hoveredVolumeSlider, true);
        }

        private void AdjustSlider(Slider slider, float delta)
        {
            if (slider == null) return;
            slider.value = Mathf.Clamp01(slider.value + delta);
        }

        private void OpenSettings()
        {
            _mainButtonGroup.SetActive(false);
            Show(_settingsSubPanel);
        }

        private void CloseSettings()
        {
            HideAll();
            _mainButtonGroup.SetActive(true);
        }

        private void Show(GameObject panel)
        {
            _settingsSubPanel.SetActive(panel == _settingsSubPanel);
            _volumeSubPanel.SetActive(panel == _volumeSubPanel);
            _controlsSubPanel.SetActive(panel == _controlsSubPanel);
            _creditsSubPanel.SetActive(panel == _creditsSubPanel);

            // 切面板前把目前反白的按鈕/滑桿實際復原，避免視覺卡住
            SetHovered(null);
            ApplyColor(_hoveredVolumeSlider, false);
            _hoveredVolumeSlider = null;
            _volumeSliderIndex   = 0;
            _lastMousePos        = null;

            if (panel == _settingsSubPanel)
                _navButtons = new[] { _btnSettingsVolume, _btnSettingsControls, _btnSettingsCredits };

            if (panel == _volumeSubPanel)
                SyncSliderValues();
        }

        private void HideAll()
        {
            _settingsSubPanel.SetActive(false);
            _volumeSubPanel.SetActive(false);
            _controlsSubPanel.SetActive(false);
            _creditsSubPanel.SetActive(false);
        }

        // ── 音量同步（與 PauseMenuUI 相同邏輯）───────────────────────────

        private void SyncSliderValues()
        {
            var gpm = GameProgressManager.Instance;
            if (gpm == null) return;
            _masterSlider?.SetValueWithoutNotify(gpm.MasterVolume);
            _musicSlider?.SetValueWithoutNotify(gpm.MusicVolume);
            _sfxSlider?.SetValueWithoutNotify(gpm.SFXVolume);
        }

        private void BindSliders()
        {
            _masterSlider?.onValueChanged.AddListener(v => GameProgressManager.Instance?.SetMasterVolume(v));
            _musicSlider?.onValueChanged.AddListener(v => GameProgressManager.Instance?.SetMusicVolume(v));
            _sfxSlider?.onValueChanged.AddListener(v => GameProgressManager.Instance?.SetSFXVolume(v));
        }
    }
}
