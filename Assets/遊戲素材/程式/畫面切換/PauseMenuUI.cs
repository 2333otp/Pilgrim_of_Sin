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
    ///   Layer 0 — ESC選單_Panel（8 個按鈕）
    ///   Layer 1 — 子面板（音量 / 操作說明 / 製作團隊），覆蓋在按鈕列上方
    ///   Layer 2 — 玩家狀態_Panel，取代 ESC選單_Panel
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

        // ── ESC 選單 8 顆按鈕 ──────────────────────────────────────────
        [Header("ESC 選單 - 按鈕")]
        [SerializeField] private Button _btnPlayerStatus;
        [SerializeField] private Button _btnSave;
        [SerializeField] private Button _btnVolume;
        [SerializeField] private Button _btnControls;
        [SerializeField] private Button _btnCredits;
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
        [SerializeField] private Button _volumeBackBtn;

        // ── 操作說明子面板元件 ─────────────────────────────────────────
        [Header("操作說明子面板")]
        [SerializeField] private Button _controlsBackBtn;

        // ── 製作團隊子面板元件 ─────────────────────────────────────────
        [Header("製作團隊子面板")]
        [SerializeField] private Button _creditsBackBtn;

        // ── 玩家狀態面板 ───────────────────────────────────────────────
        [Header("玩家狀態面板")]
        [SerializeField] private PlayerStatusUI _playerStatusUI;
        [SerializeField] private Button _btnReturnFromStatus;

        // ── 場景設定 ───────────────────────────────────────────────────
        [Header("場景設定")]
        [Tooltip("Hub 場景勾選此項，「回小木屋」按鈕會自動隱藏")]
        [SerializeField] private bool _isHubScene = false;

        // ── 內部狀態 ───────────────────────────────────────────────────
        private StateMachine.PlayerController _playerController;
        private GameObject _currentSubPanel;

        // ── Awake ──────────────────────────────────────────────────────

        private void Awake()
        {
            Instance = this;
            HideAll();

            if (_btnReturnHub != null)
                _btnReturnHub.gameObject.SetActive(!_isHubScene);

            BindSliders();
        }

        private void OnDestroy()
        {
            UnbindSliders();
        }

        // ── Update：手動滑鼠點擊偵測（相容 New Input System + timeScale=0）──

        private void Update()
        {
            bool escVisible    = _escPanel != null    && _escPanel.activeSelf;
            bool statusVisible = _playerStatusPanel != null && _playerStatusPanel.activeSelf;
            if (!escVisible && !statusVisible) return;

            if (!Mouse.current.leftButton.wasPressedThisFrame) return;

            Vector2 pos = Mouse.current.position.ReadValue();

            // ── 玩家狀態頁：只偵測「返回ESC選單」─────────────────────
            if (statusVisible)
            {
                TryClick(_btnReturnFromStatus, pos, ClosePlayerStatus);
                return;
            }

            // ── 子面板：只偵測對應返回按鈕 ───────────────────────────
            if (_currentSubPanel != null && _currentSubPanel.activeSelf)
            {
                if      (_currentSubPanel == _volumeSubPanel)   TryClick(_volumeBackBtn,   pos, CloseSubPanel);
                else if (_currentSubPanel == _controlsSubPanel) TryClick(_controlsBackBtn, pos, CloseSubPanel);
                else if (_currentSubPanel == _creditsSubPanel)  TryClick(_creditsBackBtn,  pos, CloseSubPanel);
                return;
            }

            // ── ESC 選單主按鈕列 ──────────────────────────────────────
            if (_buttonGroup != null && _buttonGroup.activeSelf)
            {
                TryClick(_btnPlayerStatus, pos, OpenPlayerStatus);
                TryClick(_btnSave,         pos, OnSaveClicked);
                TryClick(_btnVolume,       pos, () => OpenSubPanel(_volumeSubPanel));
                TryClick(_btnControls,     pos, () => OpenSubPanel(_controlsSubPanel));
                TryClick(_btnCredits,      pos, () => OpenSubPanel(_creditsSubPanel));
                TryClick(_btnResume,       pos, OnResumeClicked);
                TryClick(_btnReturnHub,    pos, OnReturnHubClicked);
                TryClick(_btnMainMenu,     pos, OnReturnMainMenuClicked);
            }
        }

        private void TryClick(Button btn, Vector2 screenPos, System.Action callback)
        {
            if (btn == null || !btn.gameObject.activeInHierarchy || !btn.interactable) return;
            var rt = btn.GetComponent<RectTransform>();
            if (RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, null))
                callback?.Invoke();
        }

        // ── 公開 API（由 PausedState 呼叫）────────────────────────────

        /// <summary>顯示 ESC 選單，並同步音量滑桿初始值。</summary>
        public void Show(StateMachine.PlayerController player)
        {
            _playerController = player;
            _escPanel?.SetActive(true);
            ShowButtonGroup();
            SyncSliderValues();
        }

        /// <summary>隱藏所有面板，恢復 timeScale。</summary>
        public void Hide()
        {
            HideAll();
            Time.timeScale = 1f;
            _playerController = null;
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

        private void OpenSubPanel(GameObject subPanel)
        {
            if (subPanel == null) return;
            _buttonGroup?.SetActive(false);
            _currentSubPanel = subPanel;
            subPanel.SetActive(true);
        }

        private void CloseSubPanel()
        {
            _currentSubPanel?.SetActive(false);
            _currentSubPanel = null;
            ShowButtonGroup();
        }

        private void OpenPlayerStatus()
        {
            _escPanel?.SetActive(false);
            _playerStatusPanel?.SetActive(true);
            _playerStatusUI?.Refresh(_playerController);
        }

        private void ClosePlayerStatus()
        {
            _playerStatusPanel?.SetActive(false);
            _escPanel?.SetActive(true);
            ShowButtonGroup();
        }

        private void ShowButtonGroup()
        {
            _buttonGroup?.SetActive(true);
            _volumeSubPanel?.SetActive(false);
            _controlsSubPanel?.SetActive(false);
            _creditsSubPanel?.SetActive(false);
            _currentSubPanel = null;
        }

        private void HideAll()
        {
            _escPanel?.SetActive(false);
            _playerStatusPanel?.SetActive(false);
        }

        // ── 按鈕事件 ───────────────────────────────────────────────────

        private void OnSaveClicked()
        {
            GameProgressManager.Instance?.Save();
        }

        private void OnResumeClicked()
        {
            _playerController?.ResumeFromPause();
        }

        private void OnReturnHubClicked()
        {
            Time.timeScale = 1f;
            HideAll();
            _playerController?.ForceExitPause();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
            SceneTransitionManager.Instance?.ReturnToHub();
        }

        private void OnReturnMainMenuClicked()
        {
            Time.timeScale = 1f;
            HideAll();
            _playerController?.ForceExitPause();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
            SceneTransitionManager.Instance?.ReturnToMainMenu();
        }

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
