using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 純程式碼鏡頭控制器（不依賴 Cinemachine）。
    /// 非鎖定：鏡頭在玩家正後方，滑鼠旋轉，以玩家為中心。
    /// 鎖定：鏡頭自動轉向讓敵人出現在畫面中央，玩家置中。
    /// Q：鎖定 / 解除。P：切換目標。R：重置鏡頭方向。
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform _player;
        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private Camera _camera;

        [Header("Camera Position")]
        [SerializeField] private float _distance = 5f;    // 玩家後方距離
        [SerializeField] private float _height = 2f;    // 玩家上方高度

        [Header("Mouse Rotation")]
        [SerializeField] private float _mouseSensitivityX = 3f;
        [SerializeField] private float _mouseSensitivityY = 2f;
        [SerializeField] private float _minPitch = -20f;     // 仰角下限
        [SerializeField] private float _maxPitch = 60f;     // 仰角上限

        [Header("Gamepad Rotation")]
        [SerializeField] private float _stickSensitivityX = 150f;
        [SerializeField] private float _stickSensitivityY = 100f;

        [Header("Lock-On Settings")]
        [SerializeField] private float _lockOnRange = 20f;
        [SerializeField] private LayerMask _enemyLayer = ~0;
        [SerializeField] private float _lockOnLookAtHeight = 2.5f;  // 鎖定時瞄準敵人的高度（從腳底算起）

        // ── 公開狀態 ─────────────────────────────────────────────────
        public bool IsLockedOn { get; private set; }
        public Transform LockTarget { get; private set; }

        // ── 內部 ─────────────────────────────────────────────────────
        private float _yaw;    // 水平角（Y 軸旋轉）
        private float _pitch;  // 垂直角（X 軸旋轉）

        private float _lockOnCooldown;
        private float _switchCooldown;
        private const float CooldownDuration = 0.3f;

        private readonly List<Transform> _switchHistory = new List<Transform>();

        // ── Unity 生命周期 ────────────────────────────────────────────

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            // 初始角度對齊玩家
            if (_player != null)
            {
                _yaw = _player.eulerAngles.y;
                _pitch = 15f;
            }
        }

        private void Update()
        {
            if (_player == null) return;
            if (Time.timeScale == 0f) return;

            // 遊戲執行中每幀強制維持游標鎖定（ESC 選單開啟時 timeScale=0 故不執行此段）
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;

            if (_lockOnCooldown > 0f) _lockOnCooldown -= Time.deltaTime;
            if (_switchCooldown > 0f) _switchCooldown -= Time.deltaTime;

            HandleLockOnInput();
            HandleSwitchInput();
            HandleResetInput();
            CheckLockTargetAlive();
        }

        private void LateUpdate()
        {
            if (_player == null || _camera == null) return;
            if (Time.timeScale == 0f) return;

            if (IsLockedOn && LockTarget != null)
                UpdateLockOnCamera();
            else
                UpdateFreeCamera();
        }

        // ── 非鎖定鏡頭 ───────────────────────────────────────────────

        private void UpdateFreeCamera()
        {
            var mouseDelta = Mouse.current?.delta.ReadValue() ?? Vector2.zero;
            var stickDelta = _input != null ? _input.CameraRotateInput : Vector2.zero;

            _yaw   += mouseDelta.x * _mouseSensitivityX
                    + stickDelta.x * _stickSensitivityX * Time.deltaTime;
            _pitch -= mouseDelta.y * _mouseSensitivityY
                    + stickDelta.y * _stickSensitivityY * Time.deltaTime;
            _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);

            ApplyCameraTransform(_yaw, _pitch);
        }

        // ── 鎖定鏡頭 ─────────────────────────────────────────────────

        private void UpdateLockOnCamera()
        {
            // 直接設定精確角度（不 Lerp）：
            // Lerp 會讓 _yaw 每幀持續追趕玩家→Boss 的變化角度，
            // 造成鏡頭軌道位置每幀微量偏移，玩家在畫面上就會抖動。
            Vector3 toEnemy = LockTarget.position - _player.position;
            _yaw   = Mathf.Atan2(toEnemy.x, toEnemy.z) * Mathf.Rad2Deg;
            _pitch = 15f;

            ApplyCameraTransform(_yaw, _pitch);
        }

        // ── 套用鏡頭位置與旋轉 ───────────────────────────────────────

        private void ApplyCameraTransform(float yaw, float pitch)
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);

            Vector3 playerMid = _player.position + Vector3.up * (_height * 0.5f);
            Vector3 targetPos = playerMid - rotation * Vector3.forward * _distance
                                  + Vector3.up * _height * 0.5f;

            _camera.transform.position = targetPos;

            // 鎖定時看向 Boss 胸前（_lockOnLookAtHeight 控制高度）；非鎖定時看向玩家
            Vector3 lookAtPoint = (IsLockedOn && LockTarget != null)
                ? LockTarget.position + Vector3.up * _lockOnLookAtHeight
                : playerMid;
            _camera.transform.LookAt(lookAtPoint);
        }

        // ── 輸入處理 ──────────────────────────────────────────────────

        private void HandleLockOnInput()
        {
            if (_input == null || _lockOnCooldown > 0f) return;
            if (!_input.LockOnPressed) return;

            if (IsLockedOn) Unlock();
            else TryLockOn();
            _lockOnCooldown = CooldownDuration;
        }

        private void HandleSwitchInput()
        {
            if (!IsLockedOn) return;
            if (_input == null || _switchCooldown > 0f) return;
            if (!_input.LockOnSwitchPressed) return;

            TrySwitchTarget();
            _switchCooldown = CooldownDuration;
        }

        private void HandleResetInput()
        {
            if (IsLockedOn) return;
            if (_input == null) return;
            if (!_input.ResetCameraPressed) return;

            // 重置鏡頭到玩家正後方
            _yaw = _player.eulerAngles.y;
            _pitch = 15f;
            Debug.Log("[Camera] 鏡頭重置。");
        }

        // ── 鎖定邏輯 ─────────────────────────────────────────────────

        private void TryLockOn()
        {
            var enemies = GetEnemiesInRange();
            if (enemies.Count == 0)
            {
                Debug.Log("[Camera] 範圍內沒有敵人。");
                return;
            }

            LockTarget = enemies[0];
            IsLockedOn = true;
            _switchHistory.Clear();
            _switchHistory.Add(LockTarget);
            Debug.Log($"[Camera] 鎖定：{LockTarget.name}");
        }

        private void Unlock()
        {
            LockTarget = null;
            IsLockedOn = false;
            _switchHistory.Clear();
            Debug.Log("[Camera] 解除鎖定。");
        }

        private void TrySwitchTarget()
        {
            var enemies = GetEnemiesInRange();
            if (enemies.Count == 0) { Unlock(); return; }

            Transform next = enemies.FirstOrDefault(e => !_switchHistory.Contains(e));
            if (next == null)
            {
                _switchHistory.Clear();
                next = enemies[0];
            }

            _switchHistory.Add(next);
            LockTarget = next;
            Debug.Log($"[Camera] 切換鎖定：{next.name}");
        }

        private void CheckLockTargetAlive()
        {
            if (!IsLockedOn || LockTarget == null) return;
            if (!LockTarget.gameObject.activeInHierarchy)
            {
                Debug.Log("[Camera] 鎖定目標消失，嘗試順延。");
                TrySwitchTarget();
            }
        }

        // ── 輔助 ─────────────────────────────────────────────────────

        private List<Transform> GetEnemiesInRange()
        {
            var hits = Physics.OverlapSphere(_player.position, _lockOnRange, _enemyLayer);
            var result = new List<Transform>();

            foreach (var hit in hits)
            {
                if (hit.transform == _player) continue;
                if (!hit.gameObject.activeInHierarchy) continue;
                if (hit.GetComponentInParent<IDamageable>() == null) continue;
                result.Add(hit.transform);
            }

            result.Sort((a, b) =>
                Vector3.Distance(_player.position, a.position)
                .CompareTo(Vector3.Distance(_player.position, b.position)));

            return result;
        }
    }
}