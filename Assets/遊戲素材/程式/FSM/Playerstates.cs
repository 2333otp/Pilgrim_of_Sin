using UnityEngine;
using UnityEngine.InputSystem;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 所有玩家狀態的抽象基底類別。
    /// 每個具體狀態繼承此類別，並實作 Enter / Update / Exit。
    /// </summary>
    public abstract class PlayerState
    {
        // ── 共用引用 ─────────────────────────────────────────────────
        protected PlayerController Player { get; private set; }
        protected PlayerStateMachine Machine { get; private set; }
        protected Animator Anim { get; private set; }
        protected PlayerInputReader Input { get; private set; }

        // 此狀態對應的枚舉值（用於外部查詢目前狀態）
        public abstract PlayerStateType StateType { get; }

        // ── 建構子 ───────────────────────────────────────────────────
        protected PlayerState(PlayerController player, PlayerStateMachine machine)
        {
            Player = player;
            Machine = machine;
            Anim = player.Animator;
            Input = player.InputReader;
        }

        // ── 生命周期 ─────────────────────────────────────────────────

        /// <summary>進入此狀態時呼叫一次。</summary>
        public virtual void Enter() { }

        /// <summary>每幀呼叫（物理前）。負責邏輯判斷與狀態轉換請求。</summary>
        public virtual void Update(float deltaTime) { }

        /// <summary>每幀呼叫（物理後）。負責移動、Rigidbody 操作。</summary>
        public virtual void FixedUpdate(float fixedDeltaTime) { }

        /// <summary>離開此狀態時呼叫一次。</summary>
        public virtual void Exit() { }

        // ── 共用輔助方法 ─────────────────────────────────────────────

        /// <summary>
        /// 請求切換到另一個狀態。
        /// 若 StateMachine 的優先級檢查不通過，此切換會被忽略。
        /// </summary>
        protected void RequestTransition(PlayerStateType next)
            => Machine.RequestTransition(next);

        /// <summary>播放 Animator 參數（Trigger）。</summary>
        protected void PlayAnimation(string triggerName)
            => Anim.SetTrigger(triggerName);

        /// <summary>設定 Animator Bool 參數。</summary>
        protected void SetAnimBool(string paramName, bool value)
            => Anim.SetBool(paramName, value);

        /// <summary>
        /// ESC 暫停偵測：PlayerInput.PausePressed 或 Keyboard 直讀（Game View 失焦時備援）。
        /// </summary>
        protected bool ShouldPause()
            => Input.PausePressed || (Keyboard.current?[Key.Escape].wasPressedThisFrame ?? false);
    }

    // ════════════════════════════════════════════════════════════════
    //  IdleState
    // ════════════════════════════════════════════════════════════════
    public class IdleState : PlayerState
    {
        public override PlayerStateType StateType => PlayerStateType.Idle;

        public IdleState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            SetAnimBool("IsMoving", false);
            SetAnimBool("IsSprinting", false);

            // 從空中落地才需要跳躍冷卻，避免落地瞬間又立刻連跳
            if (Machine.PreviousStateType == PlayerStateType.Jump
                || Machine.PreviousStateType == PlayerStateType.Fall)
            {
                Player.StartJumpCooldown();
            }
        }

        public override void Update(float dt)
        {
            if (ShouldPause()) { RequestTransition(PlayerStateType.Paused); return; }
            if (Player.IsDead) { RequestTransition(PlayerStateType.Dead); return; }
            // 特殊招式（優先1）
            if (Input.SpecialPressed && Player.CanUseSpecial)
            { RequestTransition(PlayerStateType.SpecialSkill); return; }
            // 切換武器
            if (Input.WeaponSwitchPressed)
            {
                Player.RequestWeaponSwitch(Input.WeaponSwitchIndex);
                return;
            }
            // 跳躍
            if (Input.JumpPressed && Player.CanJump) { RequestTransition(PlayerStateType.Jump); return; }
            // 翻滾
            if (Input.RollPressed) { RequestTransition(PlayerStateType.Roll); return; }
            // 攻擊
            if (Input.LightAttackPressed) { RequestTransition(PlayerStateType.LightAttack); return; }
            if (Input.HeavyAttackPressed) { RequestTransition(PlayerStateType.HeavyAttack); return; }
            // 移動
            if (Input.MoveInput.sqrMagnitude > 0.01f)
            { RequestTransition(PlayerStateType.Walk); return; }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  WalkState
    // ════════════════════════════════════════════════════════════════
    public class WalkState : PlayerState
    {
        public override PlayerStateType StateType => PlayerStateType.Walk;

        private float _holdTime;
        private const float SprintThreshold = 1.2f;

        public WalkState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            _holdTime = 0f;
            SetAnimBool("IsMoving", true);
            SetAnimBool("IsSprinting", false);
        }

        public override void Update(float dt)
        {
            if (ShouldPause()) { RequestTransition(PlayerStateType.Paused); return; }
            if (Player.IsDead) { RequestTransition(PlayerStateType.Dead); return; }
            if (Input.SpecialPressed && Player.CanUseSpecial)
            { RequestTransition(PlayerStateType.SpecialSkill); return; }
            if (Input.WeaponSwitchPressed) { Player.RequestWeaponSwitch(Input.WeaponSwitchIndex); return; }
            if (Input.JumpPressed && Player.CanJump) { RequestTransition(PlayerStateType.Jump); return; }
            if (Input.RollPressed) { RequestTransition(PlayerStateType.Roll); return; }
            if (Input.LightAttackPressed) { RequestTransition(PlayerStateType.LightAttack); return; }
            if (Input.HeavyAttackPressed) { RequestTransition(PlayerStateType.HeavyAttack); return; }

            if (Input.MoveInput.sqrMagnitude < 0.01f)
            {
                RequestTransition(PlayerStateType.Idle);
                return;
            }

            // 長按 1.2 秒 → 跑步
            _holdTime += dt;
            if (_holdTime >= SprintThreshold)
            {
                RequestTransition(PlayerStateType.Sprint);
            }
        }

        public override void FixedUpdate(float fdt)
            => Player.Move(Input.MoveInput, Player.WalkSpeed);
    }

    // ════════════════════════════════════════════════════════════════
    //  SprintState
    // ════════════════════════════════════════════════════════════════
    public class SprintState : PlayerState
    {
        public override PlayerStateType StateType => PlayerStateType.Sprint;

        public SprintState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            SetAnimBool("IsMoving", true);
            SetAnimBool("IsSprinting", true);
        }

        public override void Update(float dt)
        {
            if (ShouldPause()) { RequestTransition(PlayerStateType.Paused); return; }
            if (Player.IsDead) { RequestTransition(PlayerStateType.Dead); return; }
            if (Input.SpecialPressed && Player.CanUseSpecial)
            { RequestTransition(PlayerStateType.SpecialSkill); return; }
            if (Input.WeaponSwitchPressed) { Player.RequestWeaponSwitch(Input.WeaponSwitchIndex); return; }
            if (Input.JumpPressed && Player.CanJump) { RequestTransition(PlayerStateType.Jump); return; }
            if (Input.RollPressed) { RequestTransition(PlayerStateType.Roll); return; }
            if (Input.LightAttackPressed) { RequestTransition(PlayerStateType.LightAttack); return; }
            if (Input.HeavyAttackPressed) { RequestTransition(PlayerStateType.HeavyAttack); return; }

            if (Input.MoveInput.sqrMagnitude < 0.01f)
                RequestTransition(PlayerStateType.Walk);
        }

        public override void FixedUpdate(float fdt)
            => Player.Move(Input.MoveInput, Player.SprintSpeed);
    }

    // ════════════════════════════════════════════════════════════════
    //  JumpState
    // ════════════════════════════════════════════════════════════════
    public class JumpState : PlayerState
    {
        public override PlayerStateType StateType => PlayerStateType.Jump;

        private float _airTime; // 起跳後已經過的時間
        private const float MinAirTime = 0.15f; // 至少滯空這麼久才判斷落地

        public JumpState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            _airTime = 0f;
            PlayAnimation("Jump");
            Player.ApplyJumpForce();
        }

        public override void Update(float dt)
        {
            _airTime += dt;

            if (ShouldPause()) { RequestTransition(PlayerStateType.Paused); return; }
            if (Player.IsDead) { RequestTransition(PlayerStateType.Dead); return; }
            if (Input.SpecialPressed && Player.CanUseSpecial)
            { RequestTransition(PlayerStateType.SpecialSkill); return; }
            if (Input.LightAttackPressed) { RequestTransition(PlayerStateType.LightAttack); return; }
            if (Input.HeavyAttackPressed) { RequestTransition(PlayerStateType.HeavyAttack); return; }

            // MinAirTime 內不判斷落地，避免起跳第一幀就被 IsGrounded 拉回
            if (_airTime < MinAirTime) return;

            if (Player.IsFalling) { RequestTransition(PlayerStateType.Fall); return; }
            if (Player.IsGrounded) { RequestTransition(PlayerStateType.Idle); return; } // 矮跳直接落地
        }

        public override void FixedUpdate(float fdt)
            => Player.MoveAerial(Input.MoveInput);
    }

    // ════════════════════════════════════════════════════════════════
    //  FallState
    // ════════════════════════════════════════════════════════════════
    public class FallState : PlayerState
    {
        public override PlayerStateType StateType => PlayerStateType.Fall;

        public FallState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter() => PlayAnimation("Fall");

        public override void Update(float dt)
        {
            if (ShouldPause()) { RequestTransition(PlayerStateType.Paused); return; }
            if (Player.IsDead) { RequestTransition(PlayerStateType.Dead); return; }
            if (Input.SpecialPressed && Player.CanUseSpecial)
            { RequestTransition(PlayerStateType.SpecialSkill); return; }
            if (Input.LightAttackPressed) { RequestTransition(PlayerStateType.LightAttack); return; }
            if (Input.HeavyAttackPressed) { RequestTransition(PlayerStateType.HeavyAttack); return; }
            // 落地
            if (Player.IsGrounded) { RequestTransition(PlayerStateType.Idle); return; }
        }

        public override void FixedUpdate(float fdt)
            => Player.MoveAerial(Input.MoveInput);
    }

    // ════════════════════════════════════════════════════════════════
    //  RollState  （有無敵幀，只有 SpecialSkill 可打斷）
    // ════════════════════════════════════════════════════════════════
    public class RollState : PlayerState
    {
        public override PlayerStateType StateType => PlayerStateType.Roll;

        private float _rollTimer;

        public RollState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            _rollTimer = 0f;
            Player.SetInvincible(true);
            PlayAnimation("Roll");
            Player.ApplyRollForce(Input.MoveInput);
        }

        public override void Update(float dt)
        {
            if (ShouldPause()) { RequestTransition(PlayerStateType.Paused); return; }
            // 只有特殊招式可打斷（優先1 < 3）
            if (Input.SpecialPressed && Player.CanUseSpecial)
            { RequestTransition(PlayerStateType.SpecialSkill); return; }

            _rollTimer += dt;
            if (_rollTimer >= Player.RollDuration)
                RequestTransition(PlayerStateType.Idle);
        }

        public override void Exit()
            => Player.SetInvincible(false);
    }

    // ════════════════════════════════════════════════════════════════
    //  LightAttackState
    // ════════════════════════════════════════════════════════════════
    public class LightAttackState : PlayerState
    {
        public override PlayerStateType StateType => PlayerStateType.LightAttack;

        private bool _animDone;
        private float _timer;
        private bool _nextInputBuffered; // 動畫中提前輸入的緩衝

        [UnityEngine.Header("Temp - 無動畫時使用")]
        private const float FallbackDuration = 0.6f;

        public LightAttackState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            _animDone = false;
            _timer = FallbackDuration;
            _nextInputBuffered = false;
            PlayAnimation("LightAttack");
            Player.OnAttackAnimationEnd += HandleAnimEnd;
            Player.Combat?.StartLightAttack();
            Player.ComboBuffer.AddInput(ComboBuffer.AttackInput.Light);
        }

        public override void Update(float dt)
        {
            Player.ComboBuffer.Tick(dt);

            if (ShouldPause()) { RequestTransition(PlayerStateType.Paused); return; }
            if (Player.IsDead) { RequestTransition(PlayerStateType.Dead); return; }
            if (Input.SpecialPressed && Player.CanUseSpecial)
            { RequestTransition(PlayerStateType.SpecialSkill); return; }

            // 動畫進行中：提前記錄下一個輸入
            if (!_animDone)
            {
                _timer -= dt;
                if (Input.HeavyAttackPressed)
                {
                    Player.ComboBuffer.AddInput(ComboBuffer.AttackInput.Heavy);
                    _nextInputBuffered = true;
                }
                else if (Input.LightAttackPressed)
                {
                    Player.ComboBuffer.AddInput(ComboBuffer.AttackInput.Light);
                    _nextInputBuffered = true;
                }
                if (_timer <= 0f) _animDone = true;
                else return;
            }

            // 動畫結束：判斷是否觸發連段
            if (Player.ComboBuffer.TryGetCombo(out _))
            {
                RequestTransition(PlayerStateType.ComboAttack);
                return;
            }
            if (_nextInputBuffered)
            {
                // 有輸入但不符合連段 → 執行對應攻擊
                var lastInput = Player.ComboBuffer.LastInput;
                Player.ComboBuffer.Reset();
                RequestTransition(lastInput == ComboBuffer.AttackInput.Heavy
                    ? PlayerStateType.HeavyAttack
                    : PlayerStateType.LightAttack);
                return;
            }
            RequestTransition(PlayerStateType.Idle);
        }

        public override void Exit()
        {
            Player.OnAttackAnimationEnd -= HandleAnimEnd;
            Player.Combat?.EndAttack();
        }

        private void HandleAnimEnd() => _animDone = true;
    }

    // ════════════════════════════════════════════════════════════════
    //  HeavyAttackState
    // ════════════════════════════════════════════════════════════════
    public class HeavyAttackState : PlayerState
    {
        public override PlayerStateType StateType => PlayerStateType.HeavyAttack;

        private bool _animDone;
        private float _timer;
        private bool _nextInputBuffered;
        private const float FallbackDuration = 0.6f;

        public HeavyAttackState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            _animDone = false;
            _timer = FallbackDuration;
            _nextInputBuffered = false;
            PlayAnimation("HeavyAttack");
            Player.OnAttackAnimationEnd += HandleAnimEnd;
            Player.Combat?.StartHeavyAttack();
            Player.ComboBuffer.AddInput(ComboBuffer.AttackInput.Heavy);
        }

        public override void Update(float dt)
        {
            Player.ComboBuffer.Tick(dt);

            if (ShouldPause()) { RequestTransition(PlayerStateType.Paused); return; }
            if (Player.IsDead) { RequestTransition(PlayerStateType.Dead); return; }
            if (Input.SpecialPressed && Player.CanUseSpecial)
            { RequestTransition(PlayerStateType.SpecialSkill); return; }

            if (!_animDone)
            {
                _timer -= dt;
                if (Input.HeavyAttackPressed)
                {
                    Player.ComboBuffer.AddInput(ComboBuffer.AttackInput.Heavy);
                    _nextInputBuffered = true;
                }
                else if (Input.LightAttackPressed)
                {
                    Player.ComboBuffer.AddInput(ComboBuffer.AttackInput.Light);
                    _nextInputBuffered = true;
                }
                if (_timer <= 0f) _animDone = true;
                else return;
            }

            if (Player.ComboBuffer.TryGetCombo(out _))
            {
                RequestTransition(PlayerStateType.ComboAttack);
                return;
            }
            if (_nextInputBuffered)
            {
                var lastInput = Player.ComboBuffer.LastInput;
                Player.ComboBuffer.Reset();
                RequestTransition(lastInput == ComboBuffer.AttackInput.Heavy
                    ? PlayerStateType.HeavyAttack
                    : PlayerStateType.LightAttack);
                return;
            }
            RequestTransition(PlayerStateType.Idle);
        }

        public override void Exit()
        {
            Player.OnAttackAnimationEnd -= HandleAnimEnd;
            Player.Combat?.EndAttack();
        }

        private void HandleAnimEnd() => _animDone = true;
    }

    // ════════════════════════════════════════════════════════════════
    //  ComboAttackState  （4種 per 武器）
    // ════════════════════════════════════════════════════════════════
    public class ComboAttackState : PlayerState
    {
        public override PlayerStateType StateType => PlayerStateType.ComboAttack;

        private bool _animDone;
        private float _timer;
        private const float FallbackDuration = 0.8f;

        public ComboAttackState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            _animDone = false;
            _timer = FallbackDuration;
            int comboIndex = Player.ComboBuffer.CurrentComboIndex;
            PlayAnimation($"Combo{comboIndex}");
            Player.OnAttackAnimationEnd += HandleAnimEnd;
            Player.Combat?.StartComboAttack(comboIndex);
        }

        public override void Update(float dt)
        {
            if (ShouldPause()) { RequestTransition(PlayerStateType.Paused); return; }
            if (Player.IsDead) { RequestTransition(PlayerStateType.Dead); return; }
            if (Input.SpecialPressed && Player.CanUseSpecial)
            { RequestTransition(PlayerStateType.SpecialSkill); return; }

            if (!_animDone)
            {
                _timer -= dt;
                if (_timer <= 0f) _animDone = true;
                else return;
            }

            Player.ComboBuffer.Reset();
            RequestTransition(PlayerStateType.Idle);
        }

        public override void Exit()
        {
            Player.OnAttackAnimationEnd -= HandleAnimEnd;
            Player.ComboBuffer.Reset();
            Player.Combat?.EndAttack();
        }

        private void HandleAnimEnd() => _animDone = true;
    }


    // ════════════════════════════════════════════════════════════════
    //  SpecialSkillState  （有無敵幀，大部分幀，末尾幾幀無）
    // ════════════════════════════════════════════════════════════════
    public class SpecialSkillState : PlayerState
    {
        public override PlayerStateType StateType => PlayerStateType.SpecialSkill;

        private float _timer;
        private bool _animDone;
        private const float FallbackDuration = 1.2f; // 無動畫時的招式持續時間
        private const float InvincibleEndPercent = 0.8f; // 前80%有無敵幀

        public SpecialSkillState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            _timer = 0f;
            _animDone = false;
            Player.SetInvincible(true);
            PlayAnimation("SpecialSkill");
            Player.OnSpecialSkillAnimationEnd += HandleAnimEnd;
            Player.Combat?.StartSpecialAttack();
        }

        public override void Update(float dt)
        {
            if (ShouldPause()) { RequestTransition(PlayerStateType.Paused); return; }
            if (Player.IsDead) { RequestTransition(PlayerStateType.Dead); return; }

            _timer += dt;

            // 前80%有無敵，後20%移除（模擬末尾幾幀無無敵）
            if (_timer >= FallbackDuration * InvincibleEndPercent)
                Player.SetInvincible(false);

            if (!_animDone && _timer >= FallbackDuration)
                _animDone = true;

            if (_animDone)
                RequestTransition(PlayerStateType.SpecialSkillCooldown);
        }

        public override void Exit()
        {
            Player.SetInvincible(false);
            Player.OnSpecialSkillAnimationEnd -= HandleAnimEnd;
            Player.Combat?.EndAttack();
            Player.StartSpecialSkillCooldown();
        }

        private void HandleAnimEnd() => _animDone = true;
    }

    // ════════════════════════════════════════════════════════════════
    //  SpecialSkillCooldownState
    // ════════════════════════════════════════════════════════════════
    public class SpecialSkillCooldownState : PlayerState
    {
        public override PlayerStateType StateType => PlayerStateType.SpecialSkillCooldown;

        public SpecialSkillCooldownState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
            => RequestTransition(PlayerStateType.Idle); // 立刻回 Idle，CD 由 PlayerController 計時
    }

    // ════════════════════════════════════════════════════════════════
    //  WeaponSwitchState  （動畫有傷害，動畫中不可再切換）
    // ════════════════════════════════════════════════════════════════
    public class WeaponSwitchState : PlayerState
    {
        public override PlayerStateType StateType => PlayerStateType.WeaponSwitch;

        private bool _animDone;
        private float _timer;
        private const float FallbackDuration = 0.5f;

        public WeaponSwitchState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            _animDone = false;
            _timer = FallbackDuration;
            int weaponIndex = Player.PendingWeaponIndex;
            PlayAnimation($"WeaponSwitch_{weaponIndex}");
            Player.OnWeaponSwitchAnimationEnd += HandleAnimEnd;
            Player.SetInvincible(true);
        }

        public override void Update(float dt)
        {
            if (ShouldPause()) { RequestTransition(PlayerStateType.Paused); return; }
            if (Player.IsDead) { RequestTransition(PlayerStateType.Dead); return; }

            // 動畫進行中可更新目標武器，動畫結束後套用最後一次輸入
            if (!_animDone && Input.WeaponSwitchPressed)
                Player.BufferWeaponSwitch(Input.WeaponSwitchIndex);

            if (!_animDone)
            {
                _timer -= dt;
                if (_timer <= 0f) _animDone = true;
                else return;
            }
            RequestTransition(PlayerStateType.Idle);
        }

        public override void Exit()
        {
            Player.SetInvincible(false);
            Player.OnWeaponSwitchAnimationEnd -= HandleAnimEnd;
            Player.ApplyWeaponSwitch();
            Player.StartWeaponSwitchCooldown();
        }

        private void HandleAnimEnd() => _animDone = true;
    }

    // ════════════════════════════════════════════════════════════════
    //  DamagedState  （受傷硬直）
    // ════════════════════════════════════════════════════════════════
    public class DamagedState : PlayerState
    {
        public override PlayerStateType StateType => PlayerStateType.Damaged;

        private float _stunTimer;

        public DamagedState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            _stunTimer = 0f;
            PlayAnimation("Damaged");
        }

        public override void Update(float dt)
        {
            if (ShouldPause()) { RequestTransition(PlayerStateType.Paused); return; } 
            if (Player.IsDead) { RequestTransition(PlayerStateType.Dead); return; }
            _stunTimer += dt;
            if (_stunTimer >= Player.StunDuration)
                RequestTransition(PlayerStateType.Idle);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  DeadState  （HP 歸零）
    // ════════════════════════════════════════════════════════════════
    public class DeadState : PlayerState
    {
        public override PlayerStateType StateType => PlayerStateType.Dead;

        public DeadState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            PlayAnimation("Death");
            Player.OnDeath();
            BossResultPortal.Instance?.OnPlayerDefeated();
        }

        // 死亡後不再接受任何輸入
    }

    // ════════════════════════════════════════════════════════════════
    //  PausedState  （系統層面暫停，優先級 0）
    // ════════════════════════════════════════════════════════════════
    public class PausedState : PlayerState
    {
        public override PlayerStateType StateType => PlayerStateType.Paused;

        private PlayerStateType _resumeState;

        public PausedState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            // 保險：若 PauseCanvas 因場景編輯疏失被存成 inactive，
            // Awake() 就不會執行、Instance 會是 null。這裡主動找出來喚醒它，
            // 避免每次都要手動去場景檔案修正。
            if (PilgrimOfSin.PauseMenuUI.Instance == null)
            {
                var menu = UnityEngine.Object.FindFirstObjectByType<PilgrimOfSin.PauseMenuUI>(FindObjectsInactive.Include);
                if (menu != null) menu.gameObject.SetActive(true);
            }

            // Machine.PreviousStateType 是進入 Paused 之前的狀態
            // 過濾掉不適合恢復的狀態（Damaged / Dead / Paused 本身）
            // → 這些情況一律回 Idle
            Debug.Log("[PausedState] Enter 被呼叫！PauseMenuUI = " + PilgrimOfSin.PauseMenuUI.Instance);
            var prev = Machine.PreviousStateType;
            _resumeState = IsSafeResumeState(prev) ? prev : PlayerStateType.Idle;

            Time.timeScale = 0f;
            // 顯示暫停 UI（若場景中有 PauseMenuUI）
            PilgrimOfSin.PauseMenuUI.Instance?.Show(Player);
        }

        public override void Update(float dt)
        {
            // 游標解鎖後 Game View 可能失焦，PlayerInput 不再送 OnPause()
            // 因此直接讀取 Keyboard.current 作為備援，確保 ESC 在任何情況下都能恢復
            bool escPressed = Input.PausePressed
                              || (Keyboard.current?[Key.Escape].wasPressedThisFrame ?? false);

            if (escPressed)
            {
                if (PilgrimOfSin.PauseMenuUI.Instance != null &&
                    PilgrimOfSin.PauseMenuUI.Instance.ConsumeEscIfSubPanelOpen())
                    return;

                RequestTransition(_resumeState);
            }
        }

        public override void Exit()
        {
            // 直接重置時間與游標，不依賴 PauseMenuUI 是否存在
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            PilgrimOfSin.PauseMenuUI.Instance?.Hide();
        }

        /// <summary>
        /// 判斷是否為可安全恢復的狀態。
        /// Damaged / Dead / Paused 本身不可恢復 → 回 Idle。
        /// SpecialSkillCooldown 也不恢復（CD 計時器仍在跑，回 Idle 即可）。
        /// </summary>
        private static bool IsSafeResumeState(PlayerStateType state)
        {
            return state != PlayerStateType.Damaged
                && state != PlayerStateType.Dead
                && state != PlayerStateType.Paused
                && state != PlayerStateType.SpecialSkillCooldown;
        }
    }

}