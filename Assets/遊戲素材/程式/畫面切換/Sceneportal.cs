using UnityEngine;
using UnityEngine.InputSystem;

namespace PilgrimOfSin
{
    /// <summary>
    /// 場景傳送門觸發器。
    /// 掛在小木屋的三個入口物件上，或 Boss 房的出口上。
    ///
    /// 【小木屋用法】
    ///   Inspector 設定：
    ///     Portal Type  = ToBoss
    ///     Target Boss  = Greed / Wrath / Foolish
    ///
    /// 【Boss房用法（贏了回小木屋）】
    ///   Inspector 設定：
    ///     Portal Type  = ToHub
    ///
    /// 【互動方式】
    ///   玩家走進觸發區域後，畫面出現提示（"按 X 進入"）
    ///   按下互動鍵（預設 X）後切換場景。
    ///   也可設定 AutoTrigger = true，走進去自動切換（不需按鍵）。
    /// </summary>
    public class ScenePortal : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────
        [Header("傳送門設定")]
        [SerializeField] private PortalType _portalType = PortalType.ToBoss;
        [SerializeField] private BossType _targetBoss = BossType.Greed;

        [Header("互動設定")]
        [Tooltip("true = 走進去自動切換；false = 需按互動鍵")]
        [SerializeField] private bool _autoTrigger = false;

        [Header("提示 UI（選填）")]
        [Tooltip("走進觸發區後顯示的互動提示物件")]
        [SerializeField] private GameObject _interactPrompt;

        // ── 狀態 ─────────────────────────────────────────────────────
        private bool _playerInRange = false;
        private PlayerInput _playerInput;
        private InputAction _interactAction;

        // ── 生命週期 ──────────────────────────────────────────────────
        private void Start()
        {
            if (_interactPrompt != null)
                _interactPrompt.SetActive(false);
        }

        private void OnDestroy()
        {
            UnbindInput();
        }

        // ── 觸發區偵測 ────────────────────────────────────────────────
        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;

            _playerInRange = true;

            if (_interactPrompt != null)
                _interactPrompt.SetActive(true);

            if (_autoTrigger)
            {
                Trigger();
                return;
            }

            // 綁定互動輸入
            _playerInput = other.GetComponent<PlayerInput>();
            BindInput(_playerInput);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player")) return;

            _playerInRange = false;

            if (_interactPrompt != null)
                _interactPrompt.SetActive(false);

            UnbindInput();
        }

        // ── 新版 Input System 綁定 ────────────────────────────────────
        private void BindInput(PlayerInput playerInput)
        {
            if (playerInput == null) return;

            _interactAction = playerInput.actions["Interact"];
            if (_interactAction != null)
                _interactAction.performed += OnInteract;
        }

        private void UnbindInput()
        {
            if (_interactAction != null)
            {
                _interactAction.performed -= OnInteract;
                _interactAction = null;
            }
            _playerInput = null;
        }

        private void OnInteract(InputAction.CallbackContext ctx)
        {
            if (!_playerInRange) return;
            Trigger();
        }

        // ── 切換場景 ──────────────────────────────────────────────────
        private void Trigger()
        {
            if (SceneTransitionManager.Instance == null)
            {
                Debug.LogError("[ScenePortal] 找不到 SceneTransitionManager！請確認場景中有此物件。");
                return;
            }

            switch (_portalType)
            {
                case PortalType.ToBoss:
                    SceneTransitionManager.Instance.LoadBossScene(_targetBoss);
                    break;

                case PortalType.ToHub:
                    SceneTransitionManager.Instance.ReturnToHub();
                    break;
            }
        }

        // ── 枚舉 ──────────────────────────────────────────────────────
        public enum PortalType
        {
            ToBoss,  // 小木屋 → Boss場景
            ToHub    // Boss場景 → 小木屋
        }

#if UNITY_EDITOR
        // 編輯器用：畫出觸發範圍
        private void OnDrawGizmosSelected()
        {
            Collider col = GetComponent<Collider>();
            if (col == null) return;

            Gizmos.color = _portalType == PortalType.ToBoss
                ? new Color(1f, 0.5f, 0f, 0.4f)   // 橘色 = 進入Boss
                : new Color(0f, 0.8f, 1f, 0.4f);   // 藍色 = 回小木屋

            Gizmos.DrawCube(col.bounds.center, col.bounds.size);
        }
#endif
    }
}