using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace PilgrimOfSin
{
    /// <summary>
    /// ESC 暫停選單 UI 管理器。
    /// 掛在各場景的 PauseCanvas 物件上。
    ///
    /// 【面板層次】
    ///   Layer 0 — ESC選單_Panel（6 個按鈕）
    ///   Layer 1 — 設置選項子面板（音量設置 / 操作說明 / 製作團隊 選項）
    ///   Layer 2 — 音量 / 操作說明 / 製作團隊 / 確認框子面板
    ///   Layer 3 — 玩家狀態_Panel，取代 ESC選單_Panel
    ///
    /// 【點擊偵測】
    ///   使用 Update() + RectTransformUtility 手動偵測（New Input System + timeScale=0 相容）
    ///
    /// 【ESC 鍵行為】
    ///   有子面板或玩家狀態開著時 → 關閉上一層
    ///   只剩 ESC 選單本身時 → PausedState 恢復遊戲
    ///
    /// 【場景設定】
    ///   Is Hub Scene = true → 隱藏「回小木屋」按鈕
    /// </summary>
    public class PauseMenuUI : MonoBehaviour
    {
        public static PauseMenuUI Instance { get; private set; }

        // ── 根面板 ─────────────────────────────────────────────────────
        [Header("根面板")]
        [SerializeField] private GameObject _escPanel;
        [SerializeField] private GameObject _playerStatusPanel;

        // ── ESC 選單按鈕群組 ───────────────────────────────────────────
        [Header("ESC 選單 - 按鈕群組（顯示/隱藏整組）")]
        [SerializeField] private GameObject _buttonGroup;
        [SerializeField] private GameObject _titleText;
        [SerializeField] private GameObject _bgImage;

        // ── ESC 選單 6 顆按鈕 ──────────────────────────────────────────
        [Header("ESC 選單 - 按鈕")]
        [SerializeField] private Button _btnPlayerStatus;
        [SerializeField] private Button _btnSave;
        [SerializeField] private Button _btnSettings;      // 取代音量/操作說明/製作團隊
        [SerializeField] private Button _btnResume;
        [SerializeField] private Button _btnReturnHub;     // Hub 場景自動隱藏
        [SerializeField] private Button _btnMainMenu;

        // ── 子面板 ─────────────────────────────────────────────────────
        [Header("子面板根物件（初始隱藏）")]
        [SerializeField] private GameObject _volumeSubPanel;
        [SerializeField] private GameObject _controlsSubPanel;
        [SerializeField] private GameObject _creditsSubPanel;

        // ── 音量子面板元件 ─────────────────────────────────────────────
        [Header("音量子面板")]
        [SerializeField] private Slider _masterSlider;
        [SerializeField] private Slider _musicSlider;
        [SerializeField] private Slider _sfxSlider;

        // ── 設置選項子面板 ─────────────────────────────────────────────
        [Header("設置選項子面板（含 3 顆入口按鈕 + 返回）")]
        [SerializeField] private GameObject _settingsSubPanel;
        [SerializeField] private Button _btnSettingsVolume;
        [SerializeField] private Button _btnSettingsControls;
        [SerializeField] private Button _btnSettingsCredits;
        [SerializeField] private Button _settingsBackBtn;

        // ── 玩家狀態面板 ───────────────────────────────────────────────
        [Header("玩家狀態面板")]
        [SerializeField] private PlayerStatusUI _playerStatusUI;
        [SerializeField] private Button _btnReturnFromStatus;

        // ── 遊戲中 HUD ─────────────────────────────────────────────────
        [Header("遊戲中 HUD（暫停時隱藏）")]
        [Tooltip("武器欄根物件，暫停時自動隱藏，恢復時顯示")]
        [SerializeField] private GameObject _weaponHUD;

        // ── 存檔通知 ───────────────────────────────────────────────────
        [Header("存檔通知面板（含 CanvasGroup）")]
        [SerializeField] private GameObject _saveNotificationPanel;

        // ── 確認對話框 - 返回小木屋 ────────────────────────────────────
        [Header("確認對話框 - 返回小木屋（Boss 場景用）")]
        [SerializeField] private GameObject _returnHubConfirmPanel;
        [SerializeField] private Button _btnConfirmHub;      // 是
        [SerializeField] private Button _btnCancelHub;       // 返回遊戲

        // ── 確認對話框 - 返回主選單 ────────────────────────────────────
        [Header("確認對話框 - 返回主選單")]
        [SerializeField] private GameObject _returnMainMenuConfirmPanel;
        [SerializeField] private Button _btnConfirmMainMenu; // 是
        [SerializeField] private Button _btnCancelMainMenu;  // 返回遊戲

        // ── 場景設定 ───────────────────────────────────────────────────
        [Header("場景設定")]
        [Tooltip("Hub 場景勾選此項，「回小木屋」按鈕會自動隱藏")]
        [SerializeField] private bool _isHubScene = false;
        [Tooltip("Boss 場景勾選此項，離開前彈出「尚未破關」確認框")]
        [SerializeField] private bool _isBossScene = false;

        // ── 內部狀態 ───────────────────────────────────────────────────
        private StateMachine.PlayerController _playerController;
        private StateMachine.PlayerInputReader _inputReader;
        private GameObject _currentSubPanel;
        private GameObject _previousSubPanel; // 記錄從哪個子面板跳來的，ESC 回退用
        private Button _hoveredButton;
        private Coroutine _saveNotificationCoroutine;

        // 手把導航：追蹤當前選中的按鈕索引
        private Button[] _navButtons;
        private int _navIndex = -1;
        private float _navCooldown;

        // 存檔提示框顯示期間鎖定所有輸入
        private bool _isSaving;

        // 本場景是否已手動存過檔
        private bool _hasSaved;
        // 本場景 Boss 是否已被擊敗（由 BossResultPortal 通知）
        private bool _bossDefeatedThisSession;

        // ── Awake ──────────────────────────────────────────────────────

        private void Awake()
        {
            Instance = this;
            HideAll();

            if (_btnReturnHub != null)
                _btnReturnHub.gameObject.SetActive(!_isHubScene);

            if (_isHubScene && _weaponHUD != null)
                _weaponHUD.SetActive(false);

            BindSliders();
        }

        private void OnDestroy()
        {
            UnbindSliders();

            // 安全機制：若選單開著（timeScale=0）時場景被卸載或 Play Mode 被中止，
            // Hide()／ExecuteReturnHub()／ExecuteReturnMainMenu() 都不會執行，
            // timeScale 會永久卡在 0。此處強制恢復，避免殘留到下一次 Play。
            Time.timeScale = 1f;
        }

        // ── Update：手動滑鼠點擊偵測（相容 New Input System + timeScale=0）──

        private void Update()
        {
            bool escVisible    = _escPanel != null    && _escPanel.activeSelf;
            bool statusVisible = _playerStatusPanel != null && _playerStatusPanel.activeSelf;
            if (!escVisible && !statusVisible) return;
            if (_isSaving) return;

            if (_navCooldown > 0f) _navCooldown -= Time.unscaledDeltaTime;

            HandleGamepadMenu();

            Vector2 pos = Mouse.current.position.ReadValue();
            UpdateHover(pos);

            if (!Mouse.current.leftButton.wasPressedThisFrame) return;

            // ── 玩家狀態頁：只偵測「返回ESC選單」─────────────────────
            if (statusVisible)
            {
                TryClick(_btnReturnFromStatus, pos, ClosePlayerStatus);
                return;
            }

            // ── 子面板：只偵測對應按鈕 ────────────────────────────────
            if (_currentSubPanel != null && _currentSubPanel.activeSelf)
            {
                if (_currentSubPanel == _settingsSubPanel)
                {
                    TryClick(_btnSettingsVolume,   pos, () => OpenSubPanel(_volumeSubPanel,   _settingsSubPanel));
                    TryClick(_btnSettingsControls, pos, () => OpenSubPanel(_controlsSubPanel, _settingsSubPanel));
                    TryClick(_btnSettingsCredits,  pos, () => OpenSubPanel(_creditsSubPanel,  _settingsSubPanel));
                    TryClick(_settingsBackBtn,     pos, CloseSubPanel);
                }
                else if (_currentSubPanel == _returnHubConfirmPanel)
                {
                    TryClick(_btnConfirmHub, pos, ExecuteReturnHub);
                    TryClick(_btnCancelHub,  pos, CloseSubPanel);
                }
                else if (_currentSubPanel == _returnMainMenuConfirmPanel)
                {
                    TryClick(_btnConfirmMainMenu, pos, ExecuteReturnMainMenu);
                    TryClick(_btnCancelMainMenu,  pos, CloseSubPanel);
                }
                return;
            }

            // ── ESC 選單主按鈕列 ──────────────────────────────────────
            if (_buttonGroup != null && _buttonGroup.activeSelf)
            {
                TryClick(_btnPlayerStatus, pos, OpenPlayerStatus);
                TryClick(_btnSave,         pos, OnSaveClicked);
                TryClick(_btnSettings,     pos, () => OpenSubPanel(_settingsSubPanel));
                TryClick(_btnResume,       pos, OnResumeClicked);
                TryClick(_btnReturnHub,    pos, OnReturnHubClicked);
                TryClick(_btnMainMenu,     pos, OnReturnMainMenuClicked);
            }
        }

        // ── 手把選單導航 ───────────────────────────────────────────────

        private void HandleGamepadMenu()
        {
            if (_inputReader == null) return;

            // ── 返回（○）────────────────────────────────────────────
            if (_inputReader.MenuBackPressed)
            {
                bool consumed = ConsumeEscIfSubPanelOpen();
                if (!consumed) _playerController?.ResumeFromPause();
                return;
            }

            // ── 音量調整（方向鍵左右，音量子面板時生效）─────────────
            if (_currentSubPanel == _volumeSubPanel)
            {
                if (_inputReader.VolumeUpPressed)
                    AdjustSlider(_masterSlider, 0.05f);
                if (_inputReader.VolumeDownPressed)
                    AdjustSlider(_masterSlider, -0.05f);
            }

            // ── 上下導航 ─────────────────────────────────────────────
            if (_navCooldown > 0f) return;
            if (_navButtons == null || _navButtons.Length == 0) return;

            if (_inputReader.MenuUpPressed)
            {
                Navigate(-1);
                _navCooldown = 0.18f;
            }
            else if (_inputReader.MenuDownPressed)
            {
                Navigate(1);
                _navCooldown = 0.18f;
            }

            // ── 確認（△）────────────────────────────────────────────
            if (_inputReader.MenuConfirmPressed && _navIndex >= 0 && _navIndex < _navButtons.Length)
            {
                var btn = _navButtons[_navIndex];
                if (btn != null && btn.gameObject.activeInHierarchy && btn.interactable)
                    ExecuteButtonAction(btn);
            }
        }

        private void Navigate(int dir)
        {
            if (_navButtons == null || _navButtons.Length == 0) return;
            int start = _navIndex < 0 ? 0 : _navIndex;
            int idx   = start;
            for (int i = 0; i < _navButtons.Length; i++)
            {
                idx = (idx + dir + _navButtons.Length) % _navButtons.Length;
                var btn = _navButtons[idx];
                if (btn != null && btn.gameObject.activeInHierarchy && btn.interactable)
                {
                    SetNavSelect(idx);
                    return;
                }
            }
        }

        private void SetNavSelect(int idx)
        {
            ApplyButtonColor(_hoveredButton, false);
            _navIndex    = idx;
            _hoveredButton = _navButtons[idx];
            ApplyButtonColor(_hoveredButton, true);
        }

        private void RefreshNavButtons()
        {
            _navIndex = -1;
            _hoveredButton = null;

            bool statusVisible = _playerStatusPanel != null && _playerStatusPanel.activeSelf;
            if (statusVisible)
            {
                _navButtons = new Button[] { _btnReturnFromStatus };
                return;
            }

            if (_currentSubPanel != null && _currentSubPanel.activeSelf)
            {
                if      (_currentSubPanel == _settingsSubPanel)          _navButtons = new Button[] { _btnSettingsVolume, _btnSettingsControls, _btnSettingsCredits, _settingsBackBtn };
                else if (_currentSubPanel == _volumeSubPanel)            _navButtons = new Button[0];
                else if (_currentSubPanel == _controlsSubPanel)          _navButtons = new Button[0];
                else if (_currentSubPanel == _creditsSubPanel)           _navButtons = new Button[0];
                else if (_currentSubPanel == _returnHubConfirmPanel)     _navButtons = new Button[] { _btnConfirmHub, _btnCancelHub };
                else if (_currentSubPanel == _returnMainMenuConfirmPanel) _navButtons = new Button[] { _btnConfirmMainMenu, _btnCancelMainMenu };
                else                                                      _navButtons = new Button[0];
                return;
            }

            _navButtons = new Button[]
            {
                _btnPlayerStatus, _btnSave, _btnSettings,
                _btnResume, _btnReturnHub, _btnMainMenu
            };
        }

        private void ExecuteButtonAction(Button btn)
        {
            if      (btn == _btnPlayerStatus)     OpenPlayerStatus();
            else if (btn == _btnSave)             OnSaveClicked();
            else if (btn == _btnSettings)         OpenSubPanel(_settingsSubPanel);
            else if (btn == _btnResume)           OnResumeClicked();
            else if (btn == _btnReturnHub)        OnReturnHubClicked();
            else if (btn == _btnMainMenu)         OnReturnMainMenuClicked();
            else if (btn == _btnSettingsVolume)   OpenSubPanel(_volumeSubPanel,   _settingsSubPanel);
            else if (btn == _btnSettingsControls) OpenSubPanel(_controlsSubPanel, _settingsSubPanel);
            else if (btn == _btnSettingsCredits)  OpenSubPanel(_creditsSubPanel,  _settingsSubPanel);
            else if (btn == _settingsBackBtn)      CloseSubPanel();
            else if (btn == _btnConfirmHub)        ExecuteReturnHub();
            else if (btn == _btnCancelHub)         CloseSubPanel();
            else if (btn == _btnConfirmMainMenu)   ExecuteReturnMainMenu();
            else if (btn == _btnCancelMainMenu)    CloseSubPanel();
            else if (btn == _btnReturnFromStatus)  ClosePlayerStatus();
        }

        private void AdjustSlider(Slider slider, float delta)
        {
            if (slider == null) return;
            slider.value = Mathf.Clamp01(slider.value + delta);
        }

        private void TryClick(Button btn, Vector2 screenPos, System.Action callback)
        {
            if (btn == null || !btn.gameObject.activeInHierarchy || !btn.interactable) return;
            var rt = btn.GetComponent<RectTransform>();
            if (RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, null))
                callback?.Invoke();
        }

        // ── Hover 偵測（手動，相容 New Input System + timeScale=0）────

        private void UpdateHover(Vector2 pos)
        {
            Button newHover = GetHoveredButton(pos);
            if (newHover == _hoveredButton) return;

            ApplyButtonColor(_hoveredButton, false);
            ApplyButtonColor(newHover, true);
            _hoveredButton = newHover;
        }

        private Button GetHoveredButton(Vector2 pos)
        {
            if (_playerStatusPanel != null && _playerStatusPanel.activeSelf)
                return IsOver(_btnReturnFromStatus, pos) ? _btnReturnFromStatus : null;

            if (_currentSubPanel != null && _currentSubPanel.activeSelf)
            {
                if (_currentSubPanel == _settingsSubPanel)
                {
                    if (IsOver(_btnSettingsVolume,   pos)) return _btnSettingsVolume;
                    if (IsOver(_btnSettingsControls, pos)) return _btnSettingsControls;
                    if (IsOver(_btnSettingsCredits,  pos)) return _btnSettingsCredits;
                    if (IsOver(_settingsBackBtn,     pos)) return _settingsBackBtn;
                    return null;
                }
                if (_currentSubPanel == _volumeSubPanel)   return null;
                if (_currentSubPanel == _controlsSubPanel) return null;
                if (_currentSubPanel == _creditsSubPanel)  return null;
                if (_currentSubPanel == _returnHubConfirmPanel)
                {
                    if (IsOver(_btnConfirmHub, pos)) return _btnConfirmHub;
                    if (IsOver(_btnCancelHub,  pos)) return _btnCancelHub;
                    return null;
                }
                if (_currentSubPanel == _returnMainMenuConfirmPanel)
                {
                    if (IsOver(_btnConfirmMainMenu, pos)) return _btnConfirmMainMenu;
                    if (IsOver(_btnCancelMainMenu,  pos)) return _btnCancelMainMenu;
                    return null;
                }
                return null;
            }

            if (_buttonGroup != null && _buttonGroup.activeSelf)
            {
                Button[] candidates = { _btnPlayerStatus, _btnSave, _btnSettings,
                                        _btnResume, _btnReturnHub, _btnMainMenu };
                foreach (var btn in candidates)
                    if (IsOver(btn, pos)) return btn;
            }

            return null;
        }

        private bool IsOver(Button btn, Vector2 screenPos)
        {
            if (btn == null || !btn.gameObject.activeInHierarchy || !btn.interactable) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(
                btn.GetComponent<RectTransform>(), screenPos, null);
        }

        private void ApplyButtonColor(Button btn, bool highlighted)
        {
            if (btn == null || btn.targetGraphic == null) return;
            var colors = btn.colors;
            Color target = highlighted ? colors.highlightedColor : colors.normalColor;
            btn.targetGraphic.CrossFadeColor(target, colors.fadeDuration, true, true);
        }

        // ── 公開 API（由 PausedState 呼叫）────────────────────────────

        /// <summary>顯示 ESC 選單，並同步音量滑桿初始值。</summary>
        public void Show(StateMachine.PlayerController player)
        {
            _playerController = player;
            _inputReader = player?.InputReader;
            if (_escPanel != null) _escPanel.SetActive(true);
            ShowButtonGroup();
            SyncSliderValues();
            if (_weaponHUD != null)
                _weaponHUD.SetActive(false);
            Time.timeScale   = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
            RefreshNavButtons();
        }

        /// <summary>隱藏所有面板，恢復 timeScale。</summary>
        public void Hide()
        {
            if (_saveNotificationCoroutine != null)
            {
                StopCoroutine(_saveNotificationCoroutine);
                _saveNotificationCoroutine = null;
                if (_saveNotificationPanel != null) _saveNotificationPanel.SetActive(false);
                _isSaving = false;
            }
            HideAll();
            Time.timeScale = 1f;
            _playerController = null;
            if (!_isHubScene && _weaponHUD != null)
                _weaponHUD.SetActive(true);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }

        /// <summary>
        /// 若目前有子面板或玩家狀態頁開著，關閉上一層並回傳 true。
        /// PausedState 收到 true 時不恢復遊戲。
        /// </summary>
        public bool ConsumeEscIfSubPanelOpen()
        {
            if (_playerStatusPanel != null && _playerStatusPanel.activeSelf)
            {
                ClosePlayerStatus();
                return true;
            }
            if (_currentSubPanel != null && _currentSubPanel.activeSelf)
            {
                CloseSubPanel();
                return true;
            }
            return false;
        }

        // ── 導航邏輯 ───────────────────────────────────────────────────

        private void OpenSubPanel(GameObject subPanel, GameObject fromPanel = null)
        {
            if (subPanel == null) return;
            if (fromPanel != null)
            {
                // 從子面板進入下一層（如：設置選項 → 音量）：隱藏上一層，記錄返回點
                fromPanel.SetActive(false);
                _previousSubPanel = fromPanel;
            }
            else
            {
                // 從主按鈕列進入第一層：隱藏按鈕群組
                if (_buttonGroup != null) _buttonGroup.SetActive(false);
                if (_titleText   != null) _titleText.SetActive(false);
                if (_bgImage     != null) _bgImage.SetActive(false);
                _previousSubPanel = null;
            }
            _currentSubPanel = subPanel;
            subPanel.SetActive(true);
            RefreshNavButtons();
        }

        private void CloseSubPanel()
        {
            _currentSubPanel?.SetActive(false);
            _currentSubPanel = null;

            if (_previousSubPanel != null)
            {
                // 回到上一層子面板（如：音量面板 → 設置選項面板）
                _currentSubPanel  = _previousSubPanel;
                _previousSubPanel = null;
                _currentSubPanel.SetActive(true);
            }
            else
            {
                ShowButtonGroup();
            }
            RefreshNavButtons();
        }

        private void OpenPlayerStatus()
        {
            if (_escPanel          != null) _escPanel.SetActive(false);
            if (_playerStatusPanel != null) _playerStatusPanel.SetActive(true);
            _playerStatusUI?.Refresh(_playerController);
            RefreshNavButtons();
        }

        private void ClosePlayerStatus()
        {
            if (_playerStatusPanel != null) _playerStatusPanel.SetActive(false);
            if (_escPanel          != null) _escPanel.SetActive(true);
            ShowButtonGroup();
            RefreshNavButtons();
        }

        private void ShowButtonGroup()
        {
            if (_buttonGroup != null) _buttonGroup.SetActive(true);
            if (_titleText  != null) _titleText.SetActive(true);
            if (_bgImage    != null) _bgImage.SetActive(true);
            if (_settingsSubPanel != null) _settingsSubPanel.SetActive(false);
            if (_volumeSubPanel   != null) _volumeSubPanel.SetActive(false);
            if (_controlsSubPanel != null) _controlsSubPanel.SetActive(false);
            if (_creditsSubPanel  != null) _creditsSubPanel.SetActive(false);
            _currentSubPanel  = null;
            _previousSubPanel = null;
        }

        private void HideAll()
        {
            if (_escPanel                  != null) _escPanel.SetActive(false);
            if (_playerStatusPanel         != null) _playerStatusPanel.SetActive(false);
            if (_settingsSubPanel          != null) _settingsSubPanel.SetActive(false);
            if (_returnHubConfirmPanel     != null) _returnHubConfirmPanel.SetActive(false);
            if (_returnMainMenuConfirmPanel != null) _returnMainMenuConfirmPanel.SetActive(false);
        }

        // ── 按鈕事件 ───────────────────────────────────────────────────

        private void OnSaveClicked()
        {
            GameProgressManager.Instance?.Save();
            _hasSaved = true;
            if (_saveNotificationPanel != null)
            {
                if (_saveNotificationCoroutine != null) StopCoroutine(_saveNotificationCoroutine);
                _saveNotificationCoroutine = StartCoroutine(SaveNotificationRoutine());
            }
        }

        private IEnumerator SaveNotificationRoutine()
        {
            var cg = _saveNotificationPanel.GetComponent<CanvasGroup>();
            if (cg == null) yield break;

            _isSaving = true;
            _saveNotificationPanel.SetActive(true);

            // 淡入 0.3s
            float elapsed = 0f;
            while (elapsed < 0.3f)
            {
                elapsed += Time.unscaledDeltaTime;
                cg.alpha = Mathf.Clamp01(elapsed / 0.3f);
                yield return null;
            }
            cg.alpha = 1f;

            // 停留 1.2s
            yield return new WaitForSecondsRealtime(1.2f);

            // 淡出 0.5s
            elapsed = 0f;
            while (elapsed < 0.5f)
            {
                elapsed += Time.unscaledDeltaTime;
                cg.alpha = Mathf.Clamp01(1f - elapsed / 0.5f);
                yield return null;
            }
            cg.alpha = 0f;
            _saveNotificationPanel.SetActive(false);
            _saveNotificationCoroutine = null;
            _isSaving = false;
        }

        private void OnResumeClicked()
        {
            _playerController?.ResumeFromPause();
        }

        private void OnReturnHubClicked()
        {
            if (_isBossScene && !_bossDefeatedThisSession)
                OpenSubPanel(_returnHubConfirmPanel);
            else
                ExecuteReturnHub();
        }

        private void ExecuteReturnHub()
        {
            Time.timeScale = 1f;
            HideAll();
            _playerController?.ForceExitPause();
            SceneTransitionManager.Instance?.ReturnToHub();
        }

        private void OnReturnMainMenuClicked()
        {
            if (!_hasSaved)
                OpenSubPanel(_returnMainMenuConfirmPanel);
            else
                ExecuteReturnMainMenu();
        }

        private void ExecuteReturnMainMenu()
        {
            Time.timeScale = 1f;
            HideAll();
            _playerController?.ForceExitPause();
            SceneTransitionManager.Instance?.ReturnToMainMenu();
        }

        /// <summary>Boss 擊敗時由 BossResultPortal 呼叫，解除「尚未破關」確認框。</summary>
        public void NotifyBossDefeated() => _bossDefeatedThisSession = true;

        // ── 音量同步 ───────────────────────────────────────────────────

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

        private void UnbindSliders()
        {
            _masterSlider?.onValueChanged.RemoveAllListeners();
            _musicSlider?.onValueChanged.RemoveAllListeners();
            _sfxSlider?.onValueChanged.RemoveAllListeners();
        }
    }
}
