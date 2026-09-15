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
        [SerializeField] private float _jumpCooldown = 0.12f; // 落地後至少要等這麼久才能再次跳躍，避免連跳
        [SerializeField] private float _rollSpeed = 4.2f; // 用 MovePosition 全程等速位移，跟 Move() 同一套邏輯，讓位移速度跟動畫播放時間對齊，不會因為 Impulse+阻力衰減而提早滑到定點
        [SerializeField] private float _rollDuration = 1.2f; // Roll 動畫實際長度 1.167s，留一點餘裕
        [SerializeField] private float _aerialControl = 0.6f;

        public float WalkSpeed => _walkSpeed;
        public float SprintSpeed => _sprintSpeed;
        public float RollDuration => _rollDuration;

        // ── 戰鬥參數 ─────────────────────────────────────────────────
        [Header("Combat")]
        [SerializeField] private float _maxHp = 11000f;
        [SerializeField] private float _stunDuration = 1.2f; // Damage_* 動畫實際長度 1.167s，留一點餘裕
        [SerializeField] private float _specialCd = 5f;
        [SerializeField] private float _weaponSwitchCd = 1.5f;
        [SerializeField] private bool _enableWeaponSwitch = true;

        [Header("Ground Check")]
        [SerializeField] private float _groundCheckDistance = 0.6f; // 依角色高度調整
        [SerializeField] private LayerMask _groundLayer = ~0;       // 預設偵測所有層

        public float StunDuration => _stunDuration;

        public float CurrentHp { get; private set; }
        public float MaxHp => _maxHp;
        public bool IsDead => CurrentHp <= 0f;
        public bool IsGrounded { get; private set; }
        public bool IsFalling { get; private set; }
        // 目前 Animator 只做了 SpecialSkill_Pencil，其他武器（畫筆/畫刀/調色盤）觸發 Trigger 後
        // 找不到對應的轉場，Animator 會停在原本正在播的動畫（例如跑步）不動，玩家卻已經卡進
        // SpecialSkillState 動不了、也打不了，表現就是「跑步跑到一半按特殊技能，動畫卡在跑步上」。
        // 在對應武器的 SpecialSkill 動畫做出來之前，先把非鉛筆武器的特殊技能鎖住，避免踩到這個洞。
        public bool CanUseSpecial => _specialCdTimer <= 0f
                                      && (Combat == null || Combat.CurrentWeaponIndex == 1);
        public bool CanJump => _jumpCooldownTimer <= 0f;
        public bool EnableWeaponSwitch => _enableWeaponSwitch;
        public PlayerStateType CurrentStateType => _stateMachine?.CurrentStateType ?? PlayerStateType.Idle;

        // 武器切換
        public int PendingWeaponIndex { get; private set; }

        // 連段緩衝器
        public ComboBuffer ComboBuffer { get; private set; }

        // ── 內部計時器 ───────────────────────────────────────────────
        private float _specialCdTimer;
        private float _weaponSwitchCdTimer;
        private float _jumpCooldownTimer;

        // 無敵來源分開管理，避免善區免疫與翻滾/特殊招式無敵幀互相覆蓋
        // 只有兩個都是 false 才真正可以受傷
        private bool _isFrameInvincible; // 翻滾、特殊招式、武器切換等動作幀無敵
        private bool _isSafeZoneImmune;  // 玩家在善區內的環境免疫

        // 受到傷害倍率（1.0 = 正常；< 1.0 = 有防禦增益）
        // 由關卡 Boss 邏輯呼叫 ApplyDefenseBonus() 設定，Inspector 無法直接看到此值
        private float _incomingDamageMultiplier = 1.0f;

        private bool IsInvincible => _isFrameInvincible || _isSafeZoneImmune;

        // ── 動畫事件（各狀態訂閱） ───────────────────────────────────
        public event Action OnAttackAnimationEnd;
        public event Action OnSpecialSkillAnimationEnd;
        public event Action OnWeaponSwitchAnimationEnd;

        // ── UI 事件 ──────────────────────────────────────────────────
        public event Action<int> OnWeaponSwitched;

        // ── 狀態機 ───────────────────────────────────────────────────
        private PlayerStateMachine _stateMachine;

        // ── 鏡頭方向快取（Update 更新，FixedUpdate 使用）────────────
        private Vector3 _cachedCamForward = Vector3.forward;
        private Vector3 _cachedCamRight   = Vector3.right;

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

            // Animator 一開始就要知道目前武器，Idle/攻擊/受擊才能選到正確的分支
            Animator.SetInteger("WeaponIndex", Combat != null ? Combat.CurrentWeaponIndex : 1);

            // 開啟 Apply Root Motion，交給 OnAnimatorMove 自行決定何時採用——
            // 大部分狀態（Walk/Sprint/Roll…）都是程式碼算好速度用 Rb.MovePosition 移動，
            // 這裡開了之後也不會被 Unity 自動套用，因為同物件上有 OnAnimatorMove() 就一定走那邊。
            Animator.applyRootMotion = true;
        }

        public void ForceExitPause()
        {
            Time.timeScale = 1f;
            _stateMachine.ForceTransition(PlayerStateType.Idle);
        }

        private void Update()
        {
            EnsureStateMachine();

            // timeScale = 0 時只跑狀態機（讓 PausedState 可以偵測輸入恢復）
            _stateMachine.Update(Time.unscaledDeltaTime);

            if (Time.timeScale == 0f) return;

            CacheCameraDirections();
            UpdateTimers(Time.deltaTime);
            UpdateGroundCheck();
        }

        private void FixedUpdate()
        {
            EnsureStateMachine();
            _stateMachine.FixedUpdate(Time.fixedDeltaTime);
        }

        /// <summary>
        /// 測試時發現：Play Mode 途中如果剛好觸發腳本重新編譯（Domain Reload），Unity 不會對場景裡
        /// 已存在的物件重新呼叫 Awake()，但 _stateMachine 這種純 C# 物件（非 MonoBehaviour、沒有被
        /// 序列化）會被整個清空成 null——Update()/FixedUpdate()/OnAnimatorMove() 因此每幀丟
        /// NullReferenceException，狀態機再也不會被推進，角色會定格在 reload 當下正在播的動畫
        /// （例如跑步）上，看起來完全卡死、操作沒有任何反應。這裡偵測到 null 就重建一次自救，
        /// 代價是重建後會回到 Idle（比永久卡死好得多）。
        /// </summary>
        private void EnsureStateMachine()
        {
            if (_stateMachine != null) return;
            Debug.LogWarning("[PlayerController] _stateMachine 是 null（可能是 Play Mode 中途發生了 Domain Reload），重新建立狀態機。");
            BuildStateMachine();
        }

        /// <summary>
        /// HeavyAttack、ComboAttack 採用動畫本身的位移，讓角色停在動畫最後一幀腳下實際站的位置，
        /// 而不是動畫播完、切回 Idle 時瞬間彈回原本的 root（鉛筆/畫筆的重攻擊跟連段1~4都已經把
        /// 對應 fbx 的 lockRootPositionXZ 取消勾選，位移是真的 root motion curve，這裡才吃得到）。
        /// 其他狀態一律不採用——維持既有「全部用程式碼控制位移」的設計，避免跟 Move()/MoveRoll() 打架。
        /// </summary>
        private void OnAnimatorMove()
        {
            if (_stateMachine == null) return; // 見 EnsureStateMachine() 註解：Domain Reload 後這裡可能還沒被 Update() 救回來
            var state = _stateMachine.CurrentStateType;
            if (state == PlayerStateType.HeavyAttack || state == PlayerStateType.ComboAttack)
                Rb.MovePosition(Rb.position + Animator.deltaPosition);
        }

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
            {
                Quaternion targetRot = Quaternion.LookRotation(dir);
                Rb.MoveRotation(Quaternion.Slerp(Rb.rotation, targetRot, 10f * Time.fixedDeltaTime));
            }

            // 角色目前朝向 vs. 移動方向的局部分量，餵給 Animator 的 8 方向混合樹
            // （角色會轉身面向 dir，所以正常情況下 MoveZ 會很快貼近 1；
            //  轉身補間期間或鎖定視角時，才會出現左右/後退的分量）
            Vector3 localDir = transform.InverseTransformDirection(dir);
            Animator.SetFloat("MoveX", localDir.x);
            Animator.SetFloat("MoveZ", localDir.z);
        }

        public void MoveAerial(Vector2 input)
        {
            Vector3 dir = GetCameraRelativeDirection(input);
            Rb.AddForce(dir * _walkSpeed * _aerialControl, ForceMode.Force);
        }

        public void ApplyJumpForce()
            => Rb.AddForce(Vector3.up * _jumpForce, ForceMode.Impulse);

        /// <summary>翻滾方向在 Enter() 時鎖定一次，避免滾動途中搖桿方向改變導致軌跡跑掉。</summary>
        public Vector3 GetRollDirection(Vector2 input)
        {
            return input.sqrMagnitude > 0.01f
                   ? GetCameraRelativeDirection(input)
                   : transform.forward;
        }

        /// <summary>翻滾全程等速位移（跟 Move() 同樣用 MovePosition），取代舊版 Impulse+阻力衰減——
        /// 舊作法位移在最初 0.3~0.5 秒就衰減掉大半，導致角色看起來已經滾到定點，但 Roll 動畫其實還在播。</summary>
        public void MoveRoll(Vector3 dir)
            => Rb.MovePosition(Rb.position + dir * _rollSpeed * Time.fixedDeltaTime);

        private void CacheCameraDirections()
        {
            // 鎖定時用玩家→Boss方向作為前方，避免攝影機 LookAt 改變造成移動方向抖動
            if (_cameraController != null && _cameraController.IsLockedOn && _cameraController.LockTarget != null)
            {
                Vector3 toBoss = _cameraController.LockTarget.position - transform.position;
                _cachedCamForward = Vector3.ProjectOnPlane(toBoss, Vector3.up).normalized;
                _cachedCamRight   = Vector3.Cross(Vector3.up, _cachedCamForward).normalized;
                return;
            }

            Transform cam = Camera.main?.transform;
            if (cam == null) return;
            _cachedCamForward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            _cachedCamRight   = Vector3.ProjectOnPlane(cam.right,   Vector3.up).normalized;
        }

        private Vector3 GetCameraRelativeDirection(Vector2 input)
        {
            if (input.sqrMagnitude < 0.01f) return Vector3.zero;
            return (_cachedCamForward * input.y + _cachedCamRight * input.x).normalized;
        }

        // ────────────────────────────────────────────────────────────
        //  戰鬥公共介面
        // ────────────────────────────────────────────────────────────

        public void TakeDamage(float amount)
        {
            if (IsInvincible) return;
            CurrentHp = Mathf.Max(0f, CurrentHp - amount * _incomingDamageMultiplier);

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

        /// <summary>
        /// 嗔關卡：改第一幅畫後由 WrathBossController 呼叫。
        /// multiplier 傳入 0.8 表示受到傷害降低 20%（Inspector 由 Boss 那邊調整）。
        /// </summary>
        public void ApplyDefenseBonus(float multiplier)
        {
            _incomingDamageMultiplier = Mathf.Clamp01(multiplier);
        }

        /// <summary>關卡結束或重試時重置防禦倍率。</summary>
        public void ResetDefenseBonus() => _incomingDamageMultiplier = 1.0f;

        public void StartSpecialSkillCooldown() => _specialCdTimer = _specialCd;
        public void StartWeaponSwitchCooldown() => _weaponSwitchCdTimer = _weaponSwitchCd;
        public void StartJumpCooldown() => _jumpCooldownTimer = _jumpCooldown;

        public void RequestWeaponSwitch(int index)
        {
            if (!_enableWeaponSwitch) return;
            if (_weaponSwitchCdTimer > 0f) return;          // CD 未到
            if (Combat != null && Combat.CurrentWeaponIndex == index) return; // 已是當前武器
            PendingWeaponIndex = index;
            _stateMachine.RequestTransition(PlayerStateType.WeaponSwitch);
        }

        /// <summary>
        /// 切換動畫進行中更新待切換目標，不觸發新動畫。
        /// 動畫結束時 ApplyWeaponSwitch 會套用最後一次按的武器。
        /// </summary>
        public void BufferWeaponSwitch(int index)
        {
            if (Combat != null && Combat.CurrentWeaponIndex == index) return;
            PendingWeaponIndex = index;
        }

        public void ApplyWeaponSwitch()
        {
            if (Combat != null)
                Combat.CurrentWeaponIndex = PendingWeaponIndex;
            Animator.SetInteger("WeaponIndex", PendingWeaponIndex);
            OnWeaponSwitched?.Invoke(PendingWeaponIndex);
        }

        public void OnDeath()
        {
            // 死亡後續流程由 DeadState.Enter() 呼叫 BossResultPortal.OnPlayerDefeated() 處理
        }

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
            if (_jumpCooldownTimer > 0f) _jumpCooldownTimer -= dt;
        }

        private void UpdateGroundCheck()
        {
            IsGrounded = Physics.Raycast(transform.position, Vector3.down,
                                         _groundCheckDistance, _groundLayer);
            IsFalling = !IsGrounded && Rb.linearVelocity.y < -0.1f;
        }
    }
}