using UnityEngine;
using UnityEngine.UI;

namespace PilgrimOfSin
{
    /// <summary>
    /// 暫停選單 UI 管理器。
    /// 掛在各場景的 PauseCanvas 物件上。
    ///
    /// 【場景對應】
    ///   HubScene     → Is Hub Scene = true  → 只顯示「回主選單」，隱藏「回小木屋」
    ///   Boss 場景    → Is Hub Scene = false → 顯示「回小木屋」和「回主選單」
    ///
    /// 【運作方式】
    ///   PausedState.Enter() → PauseMenuUI.Instance.Show(playerController)
    ///   PausedState.Exit()  → PauseMenuUI.Instance.Hide()
    /// </summary>
    public class PauseMenuUI : MonoBehaviour
    {
        public static PauseMenuUI Instance { get; private set; }

        [Header("UI 根物件")]
        [SerializeField] private GameObject _pausePanel;

        [Header("按鈕")]
        [SerializeField] private Button _resumeButton;
        [SerializeField] private Button _returnHubButton;       // Boss 場景才顯示
        [SerializeField] private Button _returnMainMenuButton;

        [Header("場景設定")]
        [Tooltip("若此場景是 HubScene，勾選此項，「回小木屋」按鈕會自動隱藏")]
        [SerializeField] private bool _isHubScene = false;

        // 用來呼叫恢復狀態的 PlayerController
        private PilgrimOfSin.StateMachine.PlayerController _playerController;

        private void Awake()
        {
            Debug.Log("PauseMenuUI Awake 執行了，_resumeButton = " + _resumeButton);
            Instance = this;

            // 一開始隱藏暫停面板
            if (_pausePanel != null)
                _pausePanel.SetActive(false);

            // HubScene 隱藏「回小木屋」按鈕
            if (_returnHubButton != null)
                _returnHubButton.gameObject.SetActive(!_isHubScene);

            // 綁定按鈕事件
            _resumeButton?.onClick.AddListener(OnResumeClicked);
            _returnHubButton?.onClick.AddListener(OnReturnHubClicked);
            _returnMainMenuButton?.onClick.AddListener(OnReturnMainMenuClicked);
        }

        private void Update()
        {
            if (_pausePanel == null || !_pausePanel.activeSelf) return;

            if (UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame)
            {
                Vector2 mousePos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
                CheckButtonClick(mousePos);
            }
        }

        private void CheckButtonClick(Vector2 mousePos)
        {
            TryClick(_resumeButton, mousePos, OnResumeClicked);
            TryClick(_returnHubButton, mousePos, OnReturnHubClicked);
            TryClick(_returnMainMenuButton, mousePos, OnReturnMainMenuClicked);
        }

        private void TryClick(Button btn, Vector2 mousePos, System.Action callback)
        {
            if (btn == null || !btn.gameObject.activeSelf || !btn.interactable) return;
            RectTransform rt = btn.GetComponent<RectTransform>();
            if (RectTransformUtility.RectangleContainsScreenPoint(rt, mousePos, null))
                callback?.Invoke();
        }

        private void OnDestroy()
        {
            _resumeButton?.onClick.RemoveListener(OnResumeClicked);
            _returnHubButton?.onClick.RemoveListener(OnReturnHubClicked);
            _returnMainMenuButton?.onClick.RemoveListener(OnReturnMainMenuClicked);
        }

        // ── 公開 API（由 PausedState 呼叫）──────────────────────────

        /// <summary>顯示暫停選單，凍結時間。</summary>
        public void Show(PilgrimOfSin.StateMachine.PlayerController playerController)
        {
            _playerController = playerController;

            if (_pausePanel != null)
                _pausePanel.SetActive(true);

        }

        /// <summary>隱藏暫停選單，恢復時間。</summary>
        public void Hide()
        {
            if (_pausePanel != null)
                _pausePanel.SetActive(false);

            Time.timeScale = 1f;
            _playerController = null;
        }

        // ── 按鈕事件 ─────────────────────────────────────────────────

        private void OnResumeClicked()
        {
            Debug.Log("OnResumeClicked 被呼叫了！");
            _playerController?.ResumeFromPause();
        }

        private void OnReturnHubClicked()
        {
            Time.timeScale = 1f;
            if (_pausePanel != null) _pausePanel.SetActive(false);
            _playerController?.ForceExitPause();

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            SceneTransitionManager.Instance?.ReturnToHub();
        }

        private void OnReturnMainMenuClicked()
        {
            Time.timeScale = 1f;
            if (_pausePanel != null) _pausePanel.SetActive(false);
            _playerController?.ForceExitPause(); // 強制離開暫停狀態

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            SceneTransitionManager.Instance?.ReturnToMainMenu();
        }
    }
}