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
        [SerializeField] private float _lockOnRange = 20f;          // 一般可打物件（錢袋、畫作…）的鎖定距離
        [Tooltip("Boss 的鎖定距離。Boss 場地大，站在遠端時 20m 內找不到 Boss，按鎖定會完全沒反應。")]
        [SerializeField] private float _bossLockOnRange = 45f;
        [SerializeField] private LayerMask _enemyLayer = ~0;
        [SerializeField] private float _lockOnLookAtHeight = 2.5f;  // 鎖定時瞄準敵人的高度（從腳底算起）

        [Header("Camera Collision")]
        [SerializeField] private LayerMask _collisionMask = ~0;   // 場景牆壁/地板所在的 Layer，記得排除 Player/Enemy
        [SerializeField] private float _collisionRadius = 0.3f;   // SphereCast 半徑，避免鏡頭貼到牆面
        [SerializeField] private float _collisionBuffer = 0.2f;   // 撞到牆後，鏡頭往前多留的緩衝距離

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
            Vector3 pivot = playerMid + Vector3.up * _height * 0.5f;
            Vector3 desiredPos = pivot - rotation * Vector3.forward * _distance;

            _camera.transform.position = ResolveCameraCollision(pivot, desiredPos);

            // 鎖定時看向 Boss 胸前（_lockOnLookAtHeight 控制高度）；非鎖定時看向玩家
            Vector3 lookAtPoint = (IsLockedOn && LockTarget != null)
                ? LockTarget.position + Vector3.up * _lockOnLookAtHeight
                : playerMid;
            _camera.transform.LookAt(lookAtPoint);
        }

        // 從鏡頭軌道中心往理想鏡頭位置打 SphereCast，撞到場景就把鏡頭拉到牆面前，避免穿模看到場景背面
        private Vector3 ResolveCameraCollision(Vector3 pivot, Vector3 desiredPos)
        {
            Vector3 offset = desiredPos - pivot;
            float distance = offset.magnitude;
            if (distance < 0.0001f) return desiredPos;

            Vector3 direction = offset / distance;
            if (Physics.SphereCast(pivot, _collisionRadius, direction, out RaycastHit hit, distance, _collisionMask, QueryTriggerInteraction.Ignore))
            {
                float safeDistance = Mathf.Max(hit.distance - _collisionBuffer, _collisionRadius);
                return pivot + direction * safeDistance;
            }
            return desiredPos;
        }

        // ── 輸入處理 ──────────────────────────────────────────────────

        private void HandleLockOnInput()
        {
            if (_input == null || _lockOnCooldown > 0f) return;
            if (!_input.LockOnPressed) return;

            // 鎖定失敗（範圍內沒有目標）不進冷卻，玩家立刻再按一次不會被吃掉
            if (IsLockedOn) { Unlock(); _lockOnCooldown = CooldownDuration; }
            else if (TryLockOn()) _lockOnCooldown = CooldownDuration;
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
        }

        // ── 鎖定邏輯 ─────────────────────────────────────────────────

        private bool TryLockOn()
        {
            var enemies = GetEnemiesInRange();
            if (enemies.Count == 0)
                return false;

            LockTarget = enemies[0];
            IsLockedOn = true;
            _switchHistory.Clear();
            _switchHistory.Add(LockTarget);
            return true;
        }

        private void Unlock()
        {
            LockTarget = null;
            IsLockedOn = false;
            _switchHistory.Clear();
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
        }

        private void CheckLockTargetAlive()
        {
            if (!IsLockedOn || LockTarget == null) return;
            if (!LockTarget.gameObject.activeInHierarchy)
                TrySwitchTarget();
        }

        // ── 輔助 ─────────────────────────────────────────────────────

        private static bool IsBoss(IDamageable d)
            => d is IBossHealth || d is WrathBossController || d is FoolishBossController;

        /// <summary>
        /// 可鎖定目標，Boss 排在最前面（其次才是錢袋、畫作等一般可打物件），同類再依距離排序。
        /// · 以 IDamageable 所在物件的 Transform 當目標，不是打到的 Collider 的 Transform——
        ///   同一個物件有多個 Collider 時不會重複出現，也不會鎖到某個子物件。
        /// · Boss 用 _bossLockOnRange，一般物件用 _lockOnRange。
        /// </summary>
        private List<Transform> GetEnemiesInRange()
        {
            float searchRadius = Mathf.Max(_lockOnRange, _bossLockOnRange);
            var hits = Physics.OverlapSphere(_player.position, searchRadius, _enemyLayer);
            var best = new Dictionary<Transform, (bool boss, float dist)>();

            foreach (var hit in hits)
            {
                if (hit.transform == _player) continue;
                if (!hit.gameObject.activeInHierarchy) continue;
                var damageable = hit.GetComponentInParent<IDamageable>();
                if (damageable == null) continue;

                var owner = ((Component)damageable).transform;
                if (owner == _player) continue;
                if (damageable is IBossHealth health && health.IsDead) continue;

                bool boss = IsBoss(damageable);
                float dist = Vector3.Distance(_player.position, hit.ClosestPoint(_player.position));
                if (dist > (boss ? _bossLockOnRange : _lockOnRange)) continue;

                if (!best.TryGetValue(owner, out var prev) || dist < prev.dist)
                    best[owner] = (boss, dist);
            }

            return best
                .OrderByDescending(kv => kv.Value.boss)
                .ThenBy(kv => kv.Value.dist)
                .Select(kv => kv.Key)
                .ToList();
        }
    }
}