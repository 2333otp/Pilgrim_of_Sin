using UnityEngine;
using UnityEngine.InputSystem;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 封裝 Input System 輸入，供各狀態讀取。
    /// </summary>
    public class PlayerInputReader : MonoBehaviour
    {
        // ── 移動 ─────────────────────────────────────────────────────
        public Vector2 MoveInput { get; private set; }

        // ── 攻擊 ─────────────────────────────────────────────────────
        public bool LightAttackPressed { get; private set; }
        public bool HeavyAttackPressed { get; private set; }
        public bool SpecialPressed { get; private set; }

        // ── 動作 ─────────────────────────────────────────────────────
        public bool JumpPressed { get; private set; }
        public bool RollPressed { get; private set; }
        public bool InteractPressed { get; private set; } // 保留，互動補回時使用

        // ── 武器 ─────────────────────────────────────────────────────
        public bool WeaponSwitchPressed { get; private set; }
        public int WeaponSwitchIndex { get; private set; } // 1~4

        // ── 系統 ─────────────────────────────────────────────────────
        public bool PausePressed { get; private set; }
        public bool LockOnPressed { get; private set; }

        // ── Input Actions（請在 Inspector 中連結） ───────────────────
        [SerializeField] private InputActionReference _moveAction;
        [SerializeField] private InputActionReference _lightAttackAction;
        [SerializeField] private InputActionReference _heavyAttackAction;
        [SerializeField] private InputActionReference _specialAction;
        [SerializeField] private InputActionReference _jumpAction;
        [SerializeField] private InputActionReference _rollAction;
        [SerializeField] private InputActionReference _interactAction;
        [SerializeField] private InputActionReference _pauseAction;
        [SerializeField] private InputActionReference _lockOnAction;
        [SerializeField] private InputActionReference _weapon1Action;
        [SerializeField] private InputActionReference _weapon2Action;
        [SerializeField] private InputActionReference _weapon3Action;
        [SerializeField] private InputActionReference _weapon4Action;

        private void Update()
        {
            MoveInput = _moveAction?.action.ReadValue<Vector2>() ?? Vector2.zero;
            LightAttackPressed = _lightAttackAction?.action.WasPressedThisFrame() ?? false;
            HeavyAttackPressed = _heavyAttackAction?.action.WasPressedThisFrame() ?? false;
            SpecialPressed = _specialAction?.action.WasPressedThisFrame() ?? false;
            JumpPressed = _jumpAction?.action.WasPressedThisFrame() ?? false;
            RollPressed = _rollAction?.action.WasPressedThisFrame() ?? false;
            InteractPressed = _interactAction?.action.WasPressedThisFrame() ?? false;
            PausePressed = _pauseAction?.action.WasPressedThisFrame() ?? false;
            LockOnPressed = _lockOnAction?.action.WasPressedThisFrame() ?? false;

            // 武器切換（1~4）
            WeaponSwitchPressed = false;
            if (_weapon1Action?.action.WasPressedThisFrame() ?? false) { WeaponSwitchPressed = true; WeaponSwitchIndex = 1; }
            else if (_weapon2Action?.action.WasPressedThisFrame() ?? false) { WeaponSwitchPressed = true; WeaponSwitchIndex = 2; }
            else if (_weapon3Action?.action.WasPressedThisFrame() ?? false) { WeaponSwitchPressed = true; WeaponSwitchIndex = 3; }
            else if (_weapon4Action?.action.WasPressedThisFrame() ?? false) { WeaponSwitchPressed = true; WeaponSwitchIndex = 4; }
        }
    }
}