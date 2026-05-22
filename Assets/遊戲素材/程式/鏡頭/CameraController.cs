using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Cinemachine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// Cinemachine 3.x 鏡頭控制器。
    /// - Q：鎖定 / 解除鎖定最近敵人
    /// - P：切換鎖定目標（依距離循環，全切一輪後重置，死亡自動順延）
    /// - R：重置鏡頭方向（非鎖定時有效）
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CinemachineCamera _cinemachineCamera;
        [SerializeField] private Transform _player;
        [SerializeField] private Transform _lockOnTarget;   // 空 GameObject 當中點錨點
        [SerializeField] private PlayerInputReader _input;

        [Header("Lock-On Settings")]
        [SerializeField] private float _lockOnRange = 20f;
        [SerializeField] private LayerMask _enemyLayer = ~0;

        public bool IsLockedOn { get; private set; }
        public Transform LockTarget { get; private set; }

        // 鎖定切換：記錄已切換過的目標，循環用
        private readonly List<Transform> _switchHistory = new List<Transform>();

        private float _lockOnCooldown;
        private float _switchCooldown;
        private const float CooldownDuration = 0.3f;

        private CinemachineOrbitalFollow _orbitalFollow;
        private CinemachineInputAxisController _inputController;

        // ── Unity 生命周期 ────────────────────────────────────────────

        private void Awake()
        {
            if (_cinemachineCamera != null)
            {
                _orbitalFollow = _cinemachineCamera.GetComponent<CinemachineOrbitalFollow>();
                _inputController = _cinemachineCamera.GetComponent<CinemachineInputAxisController>();
            }
        }

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            SetTrackingTarget(_player);
        }

        private void Update()
        {
            if (_player == null) return;

            if (_lockOnCooldown > 0f) _lockOnCooldown -= Time.deltaTime;
            if (_switchCooldown > 0f) _switchCooldown -= Time.deltaTime;

            HandleLockOnInput();
            HandleLockOnSwitchInput();
            UpdateLockOnTargetPosition();

            if (Input.GetKeyDown(KeyCode.R))
                ResetCamera();
        }

        // ── 鎖定 / 解除 ───────────────────────────────────────────────

        private void HandleLockOnInput()
        {
            if (_input == null || _lockOnCooldown > 0f) return;
            if (!_input.LockOnPressed) return;

            if (IsLockedOn) Unlock();
            else TryLockOn();
            _lockOnCooldown = CooldownDuration;
        }

        private void TryLockOn()
        {
            var target = GetEnemiesInRange().FirstOrDefault();
            if (target != null)
            {
                ApplyLockTarget(target);
                _switchHistory.Clear();
                _switchHistory.Add(target);
                Debug.Log($"[Camera] 鎖定：{target.name}");
            }
            else
            {
                Debug.Log("[Camera] 範圍內沒有敵人。");
            }
        }

        private void Unlock()
        {
            LockTarget = null;
            IsLockedOn = false;
            _switchHistory.Clear();

            if (_cinemachineCamera != null)
                _cinemachineCamera.LookAt = _player;

            if (_inputController != null)
                _inputController.enabled = true;

            Debug.Log("[Camera] 解除鎖定。");
        }

        // ── 鎖定切換（P 鍵）─────────────────────────────────────────

        private void HandleLockOnSwitchInput()
        {
            if (!IsLockedOn) return;
            if (_input == null || _switchCooldown > 0f) return;
            if (!_input.LockOnSwitchPressed) return;

            TrySwitchLockTarget();
            _switchCooldown = CooldownDuration;
        }

        private void TrySwitchLockTarget()
        {
            var allEnemies = GetEnemiesInRange();
            if (allEnemies.Count == 0) { Unlock(); return; }

            // 找第一個還沒切換過的目標
            Transform next = allEnemies.FirstOrDefault(e => !_switchHistory.Contains(e));

            if (next == null)
            {
                // 全部切過了 → 重置循環，從最近的重新開始
                _switchHistory.Clear();
                next = allEnemies[0];
            }

            _switchHistory.Add(next);
            ApplyLockTarget(next);
            Debug.Log($"[Camera] 切換鎖定：{next.name}");
        }

        // ── 更新中點錨點 + 敵人死亡自動順延 ─────────────────────────

        private void UpdateLockOnTargetPosition()
        {
            if (!IsLockedOn || LockTarget == null || _lockOnTarget == null) return;

            // 敵人死亡或消失 → 自動切換下一個
            if (!LockTarget.gameObject.activeInHierarchy)
            {
                Debug.Log("[Camera] 鎖定目標消失，嘗試順延。");
                TrySwitchLockTarget();
                return;
            }

            // 更新中點錨點位置（玩家與敵人之間 + 往上偏移）
            _lockOnTarget.position = (_player.position + LockTarget.position) * 0.5f
                                     + Vector3.up;
        }

        // ── 重置鏡頭 ──────────────────────────────────────────────────

        private void ResetCamera()
        {
            if (IsLockedOn || _orbitalFollow == null) return;
            _orbitalFollow.HorizontalAxis.Value = _player.eulerAngles.y;
            Debug.Log("[Camera] 鏡頭重置。");
        }

        // ── 輔助方法 ──────────────────────────────────────────────────

        private void ApplyLockTarget(Transform target)
        {
            LockTarget = target;
            IsLockedOn = true;

            if (_cinemachineCamera != null && _lockOnTarget != null)
                _cinemachineCamera.LookAt = _lockOnTarget;

            if (_inputController != null)
                _inputController.enabled = false;
        }

        /// <summary>取得範圍內所有有效敵人，依距離由近到遠排序。</summary>
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

        private void SetTrackingTarget(Transform target)
        {
            if (_cinemachineCamera == null || target == null) return;
            _cinemachineCamera.Follow = target;
            _cinemachineCamera.LookAt = target;
        }
    }
}