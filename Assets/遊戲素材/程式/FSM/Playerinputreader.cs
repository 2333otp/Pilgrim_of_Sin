using UnityEngine;
using UnityEngine.InputSystem;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 新版 Input System（Send Messages 模式）的輸入讀取器。
    /// PlayerInput 組件會自動呼叫 On{ActionName} 方法。
    /// </summary>
    public class PlayerInputReader : MonoBehaviour
    {
        // ── 移動 ─────────────────────────────────────────────────────
        public Vector2 MoveInput { get; private set; }

        // ── 攻擊（每幀只有按下那幀為 true） ──────────────────────────
        public bool LightAttackPressed { get; private set; }
        public bool HeavyAttackPressed { get; private set; }
        public bool SpecialPressed { get; private set; }

        // ── 動作 ─────────────────────────────────────────────────────
        public bool JumpPressed { get; private set; }
        public bool RollPressed { get; private set; }

        // ── 武器切換 ─────────────────────────────────────────────────
        public bool WeaponSwitchPressed { get; private set; }
        public int WeaponSwitchIndex { get; private set; }

        // ── 系統 ─────────────────────────────────────────────────────
        public bool PausePressed { get; private set; }
        public bool LockOnPressed { get; private set; }
        public bool LockOnSwitchPressed { get; private set; }
        public bool ResetCameraPressed { get; private set; }
        public bool InteractPressed { get; private set; }

        // ── 鏡頭（右搖桿，持續值） ───────────────────────────────────
        public Vector2 CameraRotateInput { get; private set; }

        // ── 選單導航 ─────────────────────────────────────────────────
        public bool MenuUpPressed { get; private set; }
        public bool MenuDownPressed { get; private set; }
        public bool MenuConfirmPressed { get; private set; }
        public bool MenuBackPressed { get; private set; }
        public bool VolumeUpPressed { get; private set; }
        public bool VolumeDownPressed { get; private set; }

        // ── 每幀結束清除一次性按鍵 ───────────────────────────────────
        private void LateUpdate()
        {
            LightAttackPressed = false;
            HeavyAttackPressed = false;
            SpecialPressed = false;
            JumpPressed = false;
            RollPressed = false;
            WeaponSwitchPressed = false;
            PausePressed = false;
            LockOnPressed = false;
            LockOnSwitchPressed = false;
            ResetCameraPressed = false;
            InteractPressed = false;
            MenuUpPressed = false;
            MenuDownPressed = false;
            MenuConfirmPressed = false;
            MenuBackPressed = false;
            VolumeUpPressed = false;
            VolumeDownPressed = false;
        }

        // ── Send Messages 回呼（PlayerInput 自動呼叫） ───────────────

        private void OnMove(InputValue value)
            => MoveInput = value.Get<Vector2>();

        private void OnJump(InputValue value)
        { if (value.isPressed) JumpPressed = true; }

        private void OnRoll(InputValue value)
        { if (value.isPressed) RollPressed = true; }

        private void OnLightAttack(InputValue value)
        { if (value.isPressed) LightAttackPressed = true; }

        // 注意：企劃書 LightAttack 打錯成 LightAttackk，Action 名稱要跟 Asset 一致
        private void OnLightAttackk(InputValue value)
        { if (value.isPressed) LightAttackPressed = true; }

        private void OnHeavyAttack(InputValue value)
        { if (value.isPressed) HeavyAttackPressed = true; }

        private void OnSpecial(InputValue value)
        { if (value.isPressed) SpecialPressed = true; }

        private void OnWeaponSwitch1(InputValue value)
        { if (value.isPressed) { WeaponSwitchPressed = true; WeaponSwitchIndex = 1; } }

        private void OnWeaponSwitch2(InputValue value)
        { if (value.isPressed) { WeaponSwitchPressed = true; WeaponSwitchIndex = 2; } }

        private void OnWeaponSwitch3(InputValue value)
        { if (value.isPressed) { WeaponSwitchPressed = true; WeaponSwitchIndex = 3; } }

        private void OnWeaponSwitch4(InputValue value)
        { if (value.isPressed) { WeaponSwitchPressed = true; WeaponSwitchIndex = 4; } }

        private void OnPause(InputValue value)
        {
            Debug.Log("[InputReader] OnPause 被呼叫！isPressed = " + value.isPressed);
            if (value.isPressed) PausePressed = true;
        }

        private void OnLockOn(InputValue value)
        { if (value.isPressed) LockOnPressed = true; }

        private void OnLockOnSwitch(InputValue value)
        { if (value.isPressed) LockOnSwitchPressed = true; }

        private void OnResetCamera(InputValue value)
        { if (value.isPressed) ResetCameraPressed = true; }

        private void OnInteract(InputValue value)
        { if (value.isPressed) InteractPressed = true; }

        private void OnCameraRotate(InputValue value)
            => CameraRotateInput = value.Get<Vector2>();

        private void OnMenuUp(InputValue value)
        { if (value.isPressed) MenuUpPressed = true; }

        private void OnMenuDown(InputValue value)
        { if (value.isPressed) MenuDownPressed = true; }

        private void OnMenuConfirm(InputValue value)
        { if (value.isPressed) MenuConfirmPressed = true; }

        private void OnMenuBack(InputValue value)
        { if (value.isPressed) MenuBackPressed = true; }

        private void OnVolumeUp(InputValue value)
        { if (value.isPressed) VolumeUpPressed = true; }

        private void OnVolumeDown(InputValue value)
        { if (value.isPressed) VolumeDownPressed = true; }
    }
}
