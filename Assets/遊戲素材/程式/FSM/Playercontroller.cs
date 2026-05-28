using System;
using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 繆爾玩家控制器。
    /// 負責：
    ///   - 組裝並初始化 PlayerStateMachine
    ///   - 持有所有共用資料（HP、能量、武器索引等）
    ///   - 呼叫物理相關方法（Move、Jump、Roll）
    ///   - 透過事件通知各狀態動畫結束
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Animator))]
    public class PlayerController : MonoBehaviour
    {
        // ── 外部引用 ─────────────────────────────────────────────────
        [Header("References")]
        public Animator Animator { get; private set; }
        public PlayerInputReader InputReader { get; private set; }
        public Rigidbody Rb { get; private set; }
        [Header("Camera")]
        [SerializeField] private CameraController _cameraController;
        public PlayerCombat Combat { get; private set; }

        // 簡寫（各狀態常用）
        public PlayerInputReader Input => InputReader;

        // ── 移動參數 ─────────────────────────────────────────────────
        [Header("Movement")]
        [SerializeField] private float _walkSpeed = 4f;
        [SerializeField] private float _sprintSpeed = 7f;
        [SerializeField] private float _jumpForce = 8f;
        [SerializeField] private float _rollForce = 6f;
        [SerializeField] private float _rollDuration = 0.5f;
        [SerializeField] private float _aerialControl = 0.6f;

        public float WalkSpeed => _walkSpeed;
        public float SprintSpeed => _sprintSpeed;
        public float RollDuration => _rollDuration;

        // ── 戰鬥參數 ─────────────────────────────────────────────────
        [Header("Combat")]
        [SerializeField] private float _maxHp = 11000f;
        [SerializeField] private float _stunDuration = 0.4f;
        [SerializeField] private float _specialCd = 5f;
        [SerializeField] private float _weaponSwitchCd = 1.5f;

        [Header("Ground Check")]
        [SerializeField] private float _groundCheckDistance = 0.6f; // 依角色高度調整
        [SerializeField] private LayerMask _groundLayer = ~0;       // 預設偵測所有層

        public float StunDuration => _stunDuration;

        public float CurrentHp { get; private set; }
        public bool IsDead => CurrentHp <= 0f;
        public bool IsGrounded { get; private set; }
        public bool IsFalling { get; private set; }
        public bool CanUseSpecial => _specialCdTimer <= 0f;

        // 武器切換
        public int PendingWeaponIndex { get; private set; }

        // 連段緩衝器
        public ComboBuffer ComboBuffer { get; private set; }

        // ── 內部計時器 ───────────────────────────────────────────────
        private float _specialCdTimer;
        private float _weaponSwitchCdTimer;

        // 無敵來源分開管理，避免善區免疫與翻滾/特殊招式無敵幀互相覆蓋
        // 只有兩個都是 false 才真正可以受傷
        private bool _isFrameInvincible; // 翻滾、特殊招式、武器切換等動作幀無敵
        private bool _isSafeZoneImmune;  // 玩家在善區內的環境免疫

        private bool IsInvincible => _isFrameInvincible || _isSafeZoneImmune;

        // ── 動畫事件（各狀態訂閱） ───────────────────────────────────
        public event Action OnAttackAnimationEnd;
        public event Action OnSpecialSkillAnimationEnd;
        public event Action OnWeaponSwitchAnimationEnd;

        // ── 狀態機 ───────────────────────────────────────────────────
        private PlayerStateMachine _stateMachine;

        // ────────────────────────────────────────────────────────────
        //  Unity 生命周期
        // ────────────────────────────────────────────────────────────

        private void Awake()
        {
            Animator = GetComponent<Animator>();
            InputReader = GetComponent<PlayerInputReader>();
            Rb = GetComponent<Rigidbody>();
            Combat = GetComponent<PlayerCombat>();

            ComboBuffer = new ComboBuffer();
            CurrentHp = _maxHp;

            BuildStateMachine();
        }

        private void Update()
        {
            // timeScale = 0 時只跑狀態機（讓 PausedState 可以偵測輸入恢復）
            _stateMachine.Update(Time.unscaledDeltaTime);

            if (Time.timeScale == 0f) return;

            UpdateTimers(Time.deltaTime);
            UpdateGroundCheck();
        }

        private void FixedUpdate()
            => _stateMachine.FixedUpdate(Time.fixedDeltaTime);

        // ────────────────────────────────────────────────────────────
        //  狀態機組裝
        // ────────────────────────────────────────────────────────────

        private void BuildStateMachine()
        {
            _stateMachine = new PlayerStateMachine();

            var states = new Dictionary<PlayerStateType, PlayerState>
            {
                { PlayerStateType.Idle,                 new IdleState(this, _stateMachine)                 },
                { PlayerStateType.Walk,                 new WalkState(this, _stateMachine)                 },
                { PlayerStateType.Sprint,               new SprintState(this, _stateMachine)               },
                { PlayerStateType.Jump,                 new JumpState(this, _stateMachine)                 },
                { PlayerStateType.Fall,                 new FallState(this, _stateMachine)                 },
                { PlayerStateType.Roll,                 new RollState(this, _stateMachine)                 },
                { PlayerStateType.LightAttack,          new LightAttackState(this, _stateMachine)          },
                { PlayerStateType.HeavyAttack,          new HeavyAttackState(this, _stateMachine)          },
                { PlayerStateType.ComboAttack,          new ComboAttackState(this, _stateMachine)          },
                { PlayerStateType.SpecialSkill,         new SpecialSkillState(this, _stateMachine)         },
                { PlayerStateType.SpecialSkillCooldown, new SpecialSkillCooldownState(this, _stateMachine) },
                { PlayerStateType.WeaponSwitch,         new WeaponSwitchState(this, _stateMachine)         },
                { PlayerStateType.Damaged,              new DamagedState(this, _stateMachine)               },
                { PlayerStateType.Dead,                 new DeadState(this, _stateMachine)                  },
                { PlayerStateType.Paused,               new PausedState(this, _stateMachine)                },
            };

            _stateMachine.Init(states, PlayerStateType.Idle);
        }

        // ────────────────────────────────────────────────────────────
        //  物理 / 移動
        // ────────────────────────────────────────────────────────────

        public void Move(Vector2 input, float speed)
        {
            Vector3 dir = GetCameraRelativeDirection(input);
            Rb.MovePosition(Rb.position + dir * speed * Time.fixedDeltaTime);
            if (dir != Vector3.zero)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                                                       Quaternion.LookRotation(dir), 10f * Time.fixedDeltaTime);
        }

        public void MoveAerial(Vector2 input)
        {
            Vector3 dir = GetCameraRelativeDirection(input);
            Rb.AddForce(dir * _walkSpeed * _aerialControl, ForceMode.Force);
        }

        public void ApplyJumpForce()
            => Rb.AddForce(Vector3.up * _jumpForce, ForceMode.Impulse);

        public void ApplyRollForce(Vector2 input)
        {
            Vector3 dir = input.sqrMagnitude > 0.01f
                          ? GetCameraRelativeDirection(input)
                          : transform.forward;
            Rb.AddForce(dir * _rollForce, ForceMode.Impulse);
        }

        private Vector3 GetCameraRelativeDirection(Vector2 input)
        {
            if (input.sqrMagnitude < 0.01f) return Vector3.zero;

            // 直接用 Main Camera 的方向，不是 CameraManager
            Transform cam = Camera.main.transform;
            Vector3 camForward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            Vector3 camRight = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;

            return (camForward * input.y + camRight * input.x).normalized;
        }

        // ────────────────────────────────────────────────────────────
        //  戰鬥公共介面
        // ────────────────────────────────────────────────────────────

        public void TakeDamage(float amount)
        {
            if (IsInvincible) return;
            CurrentHp = Mathf.Max(0f, CurrentHp - amount);

            if (CurrentHp <= 0f)
            {
                _stateMachine.ForceTransition(PlayerStateType.Dead);
                return;
            }

            // 透過 RequestTransition 讓 DamagableStates 白名單生效
            // SpecialSkill / Roll / WeaponSwitch / Paused 下會被白名單擋住，不進入 Damaged
            _stateMachine.RequestTransition(PlayerStateType.Damaged);
        }

        /// <summary>
        /// 動作幀無敵（翻滾、特殊招式、武器切換）。
        /// 由各狀態的 Enter/Exit 呼叫。
        /// </summary>
        public void SetInvincible(bool value) => _isFrameInvincible = value;

        /// <summary>
        /// 善區環境免疫（癡 Boss 的 SafeZone）。
        /// 由 SafeZone.OnTriggerEnter/Exit 呼叫。
        /// 與動作幀無敵完全獨立，不會互相覆蓋。
        /// </summary>
        public void SetSafeZoneImmune(bool value) => _isSafeZoneImmune = value;

        public void StartSpecialSkillCooldown() => _specialCdTimer = _specialCd;
        public void StartWeaponSwitchCooldown() => _weaponSwitchCdTimer = _weaponSwitchCd;

        public void RequestWeaponSwitch(int index)
        {
            if (_weaponSwitchCdTimer > 0f) return;          // CD 未到
            if (Combat != null && Combat.CurrentWeaponIndex == index) return; // 已是當前武器
            PendingWeaponIndex = index;
            _stateMachine.RequestTransition(PlayerStateType.WeaponSwitch);
        }

        public void ApplyWeaponSwitch()
        {
            if (Combat != null)
                Combat.CurrentWeaponIndex = PendingWeaponIndex;
            Debug.Log($"[Player] 切換至武器 {PendingWeaponIndex}");
        }

        public void OnDeath()
        {
            // TODO: 觸發死亡流程（返回存檔點）
            Debug.Log("[Player] 死亡，返回存檔點。");
        }

        // 把這個方法加在 PlayerController.cs 的 OnDeath() 方法下面

        /// <summary>
        /// 由 PauseMenuUI 的「繼續遊戲」按鈕呼叫，
        /// 等同玩家再按一次 Esc 離開暫停狀態。
        /// </summary>
        public void ResumeFromPause()
        {
            _stateMachine.RequestTransition(PlayerStateType.Idle);
        }

        // ────────────────────────────────────────────────────────────
        //  動畫事件（由 Animation Event 呼叫）
        // ────────────────────────────────────────────────────────────

        public void AnimEvent_AttackEnd() => OnAttackAnimationEnd?.Invoke();
        public void AnimEvent_SpecialEnd() => OnSpecialSkillAnimationEnd?.Invoke();
        public void AnimEvent_WeaponSwitchEnd() => OnWeaponSwitchAnimationEnd?.Invoke();
        /// <summary>動畫事件：特殊招式末尾幾幀移除無敵。</summary>
        public void AnimEvent_RemoveInvincible() => SetInvincible(false);

        // ────────────────────────────────────────────────────────────
        //  內部更新
        // ────────────────────────────────────────────────────────────

        private void UpdateTimers(float dt)
        {
            if (_specialCdTimer > 0f) _specialCdTimer -= dt;
            if (_weaponSwitchCdTimer > 0f) _weaponSwitchCdTimer -= dt;
        }

        private void UpdateGroundCheck()
        {
            IsGrounded = Physics.Raycast(transform.position, Vector3.down,
                                         _groundCheckDistance, _groundLayer);
            IsFalling = !IsGrounded && Rb.linearVelocity.y < -0.1f;
        }
    }
}