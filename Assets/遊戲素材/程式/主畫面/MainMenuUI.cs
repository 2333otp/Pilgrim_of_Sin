using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace PilgrimOfSin
{
    /// <summary>
    /// 主選單（開啟新遊戲／繼續進度／設置選單／結束遊戲）。
    ///
    /// 手把/滑鼠反白統一由本腳本手動控制（比照 ESC 選單 PauseMenuUI 的作法），
    /// 不使用 Unity 內建 EventSystem 的 Selected／Highlighted 自動切換 ——
    /// 那套機制在「手把選中」跟「滑鼠 hover」上是兩種不同的視覺狀態，
    /// 兩者同時作用時 Selected 優先權高於 Highlighted，會讓手把選取的反白顏色
    /// 跟滑鼠對不上，而且滑鼠移動不會自動更新 EventSystem 的選取物件，
    /// 導致「手把→滑鼠→手把」切換時选取跟不上。
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        [SerializeField] private Button _startGameButton;   // 開啟新遊戲
        [SerializeField] private Button _continueButton;    // 繼續進度
        [SerializeField] private Button _settingsButton;    // 設置選單（開啟行為由 MainMenuSettingsPanel 綁定）
        [SerializeField] private Button _quitButton;         // 結束遊戲

        private const float SelectedButtonScale = 1.08f;
        private const float NavRepeatDelay = 0.18f;

        private Button[] _navButtons;
        private int _navIndex = -1;
        private Button _hoveredButton;
        private Vector2? _lastMousePos;
        private float _navCooldown;

        private void Start()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;

            // 移除舊的 Inspector 綁定（避免重複呼叫）
            _startGameButton.onClick.RemoveAllListeners();

            // 動態綁定：透過 singleton 取得，不依賴 Inspector 跨場景引用
            _startGameButton.onClick.AddListener(() =>
            {
                SceneTransitionManager.Instance.LoadCutscene();
            });

            bool hasSave = GameProgressManager.SaveFileExists();
            _continueButton.gameObject.SetActive(hasSave);
            if (hasSave)
            {
                _continueButton.onClick.RemoveAllListeners();
                _continueButton.onClick.AddListener(() =>
                {
                    SceneTransitionManager.Instance.LoadHubScene();
                });
            }

            _quitButton.onClick.RemoveAllListeners();
            _quitButton.onClick.AddListener(QuitGame);

            _navButtons = hasSave
                ? new[] { _startGameButton, _continueButton, _settingsButton, _quitButton }
                : new[] { _startGameButton, _settingsButton, _quitButton };

            // 手把/鍵盤導航需要一個初始選取目標才能上下移動，
            // 沒有預先選取的話玩家按方向鍵完全沒反應。
            // 有存檔優先選「繼續進度」，沒有就選「開啟新遊戲」。
            int initialIndex = hasSave ? System.Array.IndexOf(_navButtons, _continueButton) : 0;
            SetNavSelect(initialIndex);
        }

        private void Update()
        {
            if (_navCooldown > 0f) _navCooldown -= Time.unscaledDeltaTime;

            HandleGamepadNav();

            if (Mouse.current == null) return;

            // 滑鼠沒有實際移動就不重新偵測 hover，避免蓋掉手把剛選好的按鈕
            // （道理跟 PauseMenuUI 的 hover 修正一致）。
            // 第一次 Update() 只記錄座標、不做 hover 判定：Mouse.current 在 Start()
            // 當下可能還沒穩定，若在 Start() 就先讀一次座標來當基準，容易跟這裡
            // 讀到的值對不上，反而讓 Start() 設好的預設選取在第一影格被蓋掉。
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

            if (!Mouse.current.leftButton.wasPressedThisFrame) return;
            TryClick(pos);
        }

        // ── 手把導航（無 PlayerInputReader 可用，直接讀 Gamepad.current，比照 CreditsPageBook）──

        private void HandleGamepadNav()
        {
            if (Gamepad.current == null || _navButtons == null || _navButtons.Length == 0) return;

            if (_navCooldown <= 0f)
            {
                float stickY = Gamepad.current.leftStick.ReadValue().y;
                bool up   = Gamepad.current.dpad.up.isPressed   || stickY > 0.5f;
                bool down = Gamepad.current.dpad.down.isPressed || stickY < -0.5f;

                if (up)
                {
                    Navigate(-1);
                    _navCooldown = NavRepeatDelay;
                }
                else if (down)
                {
                    Navigate(1);
                    _navCooldown = NavRepeatDelay;
                }
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
                    SetNavSelect(idx);
                    return;
                }
            }
        }

        private void SetNavSelect(int idx)
        {
            if (_navButtons == null || idx < 0 || idx >= _navButtons.Length) return;
            SetHovered(_navButtons[idx]);
        }

        /// <summary>唯一能改變「目前反白的按鈕」的地方，滑鼠 hover 跟手把上下導航都要走這裡。</summary>
        private void SetHovered(Button btn)
        {
            if (btn == _hoveredButton) return;
            ApplyButtonColor(_hoveredButton, false);
            _hoveredButton = btn;
            ApplyButtonColor(_hoveredButton, true);
            _navIndex = (_navButtons != null && btn != null) ? System.Array.IndexOf(_navButtons, btn) : -1;
        }

        private void ApplyButtonColor(Selectable btn, bool highlighted)
        {
            if (btn == null || btn.targetGraphic == null) return;
            var colors = btn.colors;
            Color target = highlighted ? colors.highlightedColor : colors.normalColor;
            btn.targetGraphic.color = target;
            btn.transform.localScale = highlighted ? Vector3.one * SelectedButtonScale : Vector3.one;
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

        private void TryClick(Vector2 screenPos)
        {
            if (_navButtons == null) return;
            foreach (var btn in _navButtons)
            {
                if (IsOver(btn, screenPos))
                {
                    btn.onClick.Invoke();
                    return;
                }
            }
        }

        private void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
