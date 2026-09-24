using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Users;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 新版 Input System（Send Messages 模式）的輸入讀取器。
    /// PlayerInput 組件會自動呼叫 On{ActionName} 方法。
    /// </summary>
    [RequireComponent(typeof(PlayerInput))]
    public class PlayerInputReader : MonoBehaviour
    {
        // ── 裝置配對修正 ─────────────────────────────────────────────
        // 專案的 PlayerInputActions 定義了 Control Scheme 後，PlayerInput 只會回應
        // 「目前 currentControlScheme 對應 group」的 binding，其他 group 的 binding
        // 即使裝置已連接，事件也會被完全遮蔽、不會觸發任何 SendMessage 回呼。
        //
        // 【踩過的坑】一開始這裡曾經在 OnEnable 時主動把手把也預先配對進去，
        // 結果反而讓問題更嚴重：Unity 的自動裝置切換是靠 InputUser.onUnpairedDeviceUsed
        // 偵測「未配對裝置的操作」來觸發的——一旦手把被提前配對，它就永遠不會被
        // 判定為「未配對裝置」，導致 currentControlScheme 永遠切不到 "Gamepad"，
        // 手把對應的所有按鍵綁定（含攻擊、跳躍、翻滾、Pause…）從此完全失效，
        // 而且比原本沒修的狀況更難發現。
        //
        // 正確做法：鍵盤/滑鼠是預設一開始就能用的裝置，直接配對；手把則刻意
        // 保持「未配對」，改成訂閱 InputUser.onUnpairedDeviceUsed，偵測到玩家
        // 第一次操作手把時，才呼叫 PerformPairingWithDevice 觸發配對＋切換 Scheme。
        private PlayerInput _playerInput;

        private void Awake() => _playerInput = GetComponent<PlayerInput>();

        private void OnEnable()
        {
            if (Keyboard.current != null) PairDevice(Keyboard.current);
            if (Mouse.current != null) PairDevice(Mouse.current);

            InputUser.onUnpairedDeviceUsed += OnUnpairedDeviceUsed;
        }

        private void OnDisable() => InputUser.onUnpairedDeviceUsed -= OnUnpairedDeviceUsed;

        private void OnUnpairedDeviceUsed(InputControl control, InputEventPtr eventPtr)
        {
            if (control.device is Gamepad gamepad)
                PairDevice(gamepad);
        }

        private void PairDevice(InputDevice device)
        {
            var paired = _playerInput.user.pairedDevices;
            for (int i = 0; i < paired.Count; i++)
                if (paired[i] == device) return;

            InputUser.PerformPairingWithDevice(device, _playerInput.user);
        }

        // 保險機制：onUnpairedDeviceUsed 理論上會在玩家第一次操作手把時自動觸發配對，
        // 但這個機制依賴 Unity 內部事件時序，曾在測試環境下出現沒有如預期觸發的情況。
        // 這裡額外主動輪詢手把是否有按鍵操作，確保就算內建機制沒生效，手把也一定能用。
        private void Update()
        {
            var gamepad = Gamepad.current;
            if (gamepad == null || _playerInput.currentControlScheme == "Gamepad") return;

            foreach (var control in gamepad.allControls)
            {
                if (control is UnityEngine.InputSystem.Controls.ButtonControl button && button.wasPressedThisFrame)
                {
                    PairDevice(gamepad);
                    _playerInput.SwitchCurrentControlScheme("Gamepad", gamepad);
                    break;
                }
            }
        }

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
        public bool MenuPageLeftPressed { get; private set; }
        public bool MenuPageRightPressed { get; private set; }

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
            MenuPageLeftPressed = false;
            MenuPageRightPressed = false;
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

        private void OnMenuPageLeft(InputValue value)
        { if (value.isPressed) MenuPageLeftPressed = true; }

        private void OnMenuPageRight(InputValue value)
        { if (value.isPressed) MenuPageRightPressed = true; }
    }
}
