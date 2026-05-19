using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
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
        }

        public override void Update(float dt)
        {
            // 暫停（最高優先）
            if (Input.PausePressed) { RequestTransition(PlayerStateType.Paused); return; }
            // 死亡檢查
            if (Player.IsDead) { RequestTransition(PlayerStateType.Dead); return; }
            // 特殊招式（優先1）
            if (Input.SpecialPressed && Player.CanUseSpecial)
            { RequestTransition(PlayerStateType.SpecialSkill); return; }
            // 切換武器
            if (Input.WeaponSwitchPressed) { RequestTransition(PlayerStateType.WeaponSwitch); return; }
            // 跳躍
            if (Input.JumpPressed) { RequestTransition(PlayerStateType.Jump); return; }
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
            if (Input.PausePressed) { RequestTransition(PlayerStateType.Paused); return; }
            if (Player.IsDead) { RequestTransition(PlayerStateType.Dead); return; }
            if (Input.SpecialPressed && Player.CanUseSpecial)
            { RequestTransition(PlayerStateType.SpecialSkill); return; }
            if (Input.WeaponSwitchPressed) { RequestTransition(PlayerStateType.WeaponSwitch); return; }
            if (Input.JumpPressed) { RequestTransition(PlayerStateType.Jump); return; }
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
            if (Input.PausePressed) { RequestTransition(PlayerStateType.Paused); return; }
            if (Player.IsDead) { RequestTransition(PlayerStateType.Dead); return; }
            if (Input.SpecialPressed && Player.CanUseSpecial)
            { RequestTransition(PlayerStateType.SpecialSkill); return; }
            if (Input.JumpPressed) { RequestTransition(PlayerStateType.Jump); return; }
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

        public JumpState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            PlayAnimation("Jump");
            Player.ApplyJumpForce();
        }

        public override void Update(float dt)
        {
            if (Input.PausePressed) { RequestTransition(PlayerStateType.Paused); return; }
            if (Player.IsDead) { RequestTransition(PlayerStateType.Dead); return; }
            // 特殊招式可打斷跳躍（優先1 < 3）
            if (Input.SpecialPressed && Player.CanUseSpecial)
            { RequestTransition(PlayerStateType.SpecialSkill); return; }
            // 空中攻擊
            if (Input.LightAttackPressed) { RequestTransition(PlayerStateType.LightAttack); return; }
            if (Input.HeavyAttackPressed) { RequestTransition(PlayerStateType.HeavyAttack); return; }
            // 頂點後轉 Fall
            if (Player.IsFalling) { RequestTransition(PlayerStateType.Fall); return; }
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
            if (Input.PausePressed) { RequestTransition(PlayerStateType.Paused); return; }
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
            if (Input.PausePressed) { RequestTransition(PlayerStateType.Paused); return; }
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

        public LightAttackState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            _animDone = false;
            PlayAnimation("LightAttack");
            Player.OnAttackAnimationEnd += HandleAnimEnd;
        }

        public override void Update(float dt)
        {
            if (Input.PausePressed) { RequestTransition(PlayerStateType.Paused); return; }
            if (Player.IsDead) { RequestTransition(PlayerStateType.Dead); return; }
            // 特殊招式打斷普攻（最高優先）
            if (Input.SpecialPressed && Player.CanUseSpecial)
            { RequestTransition(PlayerStateType.SpecialSkill); return; }
            if (!_animDone) return;

            // 動畫結束後接下一個輸入
            if (Input.HeavyAttackPressed)
            {
                // 查詢連段緩衝器
                if (Player.ComboBuffer.TryGetCombo(out var comboType))
                    RequestTransition(PlayerStateType.ComboAttack);
                else
                    RequestTransition(PlayerStateType.HeavyAttack);
                return;
            }
            if (Input.LightAttackPressed)
            {
                if (Player.ComboBuffer.TryGetCombo(out _))
                    RequestTransition(PlayerStateType.ComboAttack);
                else
                    RequestTransition(PlayerStateType.LightAttack); // 可連續施放
                return;
            }
            RequestTransition(PlayerStateType.Idle);
        }

        public override void Exit()
            => Player.OnAttackAnimationEnd -= HandleAnimEnd;

        private void HandleAnimEnd() => _animDone = true;
    }

    // ════════════════════════════════════════════════════════════════
    //  HeavyAttackState
    // ════════════════════════════════════════════════════════════════
    public class HeavyAttackState : PlayerState
    {
        public override PlayerStateType StateType => PlayerStateType.HeavyAttack;

        private bool _animDone;

        public HeavyAttackState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            _animDone = false;
            PlayAnimation("HeavyAttack");
            Player.OnAttackAnimationEnd += HandleAnimEnd;
        }

        public override void Update(float dt)
        {
            if (Input.PausePressed) { RequestTransition(PlayerStateType.Paused); return; }
            if (Player.IsDead) { RequestTransition(PlayerStateType.Dead); return; }
            if (Input.SpecialPressed && Player.CanUseSpecial)
            { RequestTransition(PlayerStateType.SpecialSkill); return; }
            if (!_animDone) return;

            if (Player.ComboBuffer.TryGetCombo(out _))
            {
                RequestTransition(PlayerStateType.ComboAttack);
                return;
            }
            if (Input.HeavyAttackPressed) { RequestTransition(PlayerStateType.HeavyAttack); return; }
            if (Input.LightAttackPressed) { RequestTransition(PlayerStateType.LightAttack); return; }
            RequestTransition(PlayerStateType.Idle);
        }

        public override void Exit()
            => Player.OnAttackAnimationEnd -= HandleAnimEnd;

        private void HandleAnimEnd() => _animDone = true;
    }

    // ════════════════════════════════════════════════════════════════
    //  ComboAttackState  （4種 per 武器）
    // ════════════════════════════════════════════════════════════════
    public class ComboAttackState : PlayerState
    {
        public override PlayerStateType StateType => PlayerStateType.ComboAttack;

        private bool _animDone;

        public ComboAttackState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            _animDone = false;
            int comboIndex = Player.ComboBuffer.CurrentComboIndex;
            PlayAnimation($"Combo{comboIndex}");
            Player.OnAttackAnimationEnd += HandleAnimEnd;
        }

        public override void Update(float dt)
        {
            if (Input.PausePressed) { RequestTransition(PlayerStateType.Paused); return; }
            if (Player.IsDead) { RequestTransition(PlayerStateType.Dead); return; }
            // 特殊招式可打斷連段
            if (Input.SpecialPressed && Player.CanUseSpecial)
            { RequestTransition(PlayerStateType.SpecialSkill); return; }
            // 被打斷（Damaged 狀態由受傷系統透過 ForceTransition 觸發）
            if (!_animDone) return;

            Player.ComboBuffer.Reset();
            RequestTransition(PlayerStateType.Idle);
        }

        public override void Exit()
        {
            Player.OnAttackAnimationEnd -= HandleAnimEnd;
            Player.ComboBuffer.Reset();
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

        public SpecialSkillState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            _timer = 0f;
            Player.SetInvincible(true);
            PlayAnimation("SpecialSkill");
            Player.OnSpecialSkillAnimationEnd += HandleAnimEnd;
        }

        public override void Update(float dt)
        {
            if (Input.PausePressed) { RequestTransition(PlayerStateType.Paused); return; }
            if (Player.IsDead) { RequestTransition(PlayerStateType.Dead); return; }
            _timer += dt;
            // 末尾幾幀移除無敵（在 PlayerController 中由動畫事件觸發）
        }

        public override void Exit()
        {
            Player.SetInvincible(false);
            Player.OnSpecialSkillAnimationEnd -= HandleAnimEnd;
            Player.StartSpecialSkillCooldown();
        }

        private void HandleAnimEnd()
            => RequestTransition(PlayerStateType.SpecialSkillCooldown);
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

        public WeaponSwitchState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            _animDone = false;
            int weaponIndex = Player.PendingWeaponIndex;
            PlayAnimation($"WeaponSwitch_{weaponIndex}");
            Player.OnWeaponSwitchAnimationEnd += HandleAnimEnd;
            // 動畫中武器切換輸入無效（由狀態機本身攔截）
        }

        public override void Update(float dt)
        {
            if (Input.PausePressed) { RequestTransition(PlayerStateType.Paused); return; }
            if (Player.IsDead) { RequestTransition(PlayerStateType.Dead); return; }
            // 動畫中忽略 WeaponSwitch 輸入（優先級系統會攔截）
            if (!_animDone) return;
            RequestTransition(PlayerStateType.Idle);
        }

        public override void Exit()
        {
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
            // Machine.PreviousStateType 是進入 Paused 之前的狀態
            // 過濾掉不適合恢復的狀態（Damaged / Dead / Paused 本身）
            // → 這些情況一律回 Idle
            var prev = Machine.PreviousStateType;
            _resumeState = IsSafeResumeState(prev) ? prev : PlayerStateType.Idle;

            Time.timeScale = 0f;
            // TODO: 顯示暫停 UI
        }

        public override void Update(float dt)
        {
            // timeScale = 0，用 unscaled delta time 偵測輸入
            if (Input.PausePressed)
                RequestTransition(_resumeState);
        }

        public override void Exit()
            => Time.timeScale = 1f;

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