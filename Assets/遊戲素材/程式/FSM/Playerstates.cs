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

        /// <summary>直接以狀態名稱切換 Animator（不經過 Transition 條件，適合依武器等資料切換待機姿勢）。</summary>
        protected void CrossFadeAnimation(string stateName, float duration = 0.15f)
            => Anim.CrossFade(stateName, duration);

        /// <summary>依 Combat.CurrentWeaponIndex（1~4）對應到 Animator 裡武器後綴，跟各 state 命名一致。</summary>
        protected static string WeaponSuffix(int weaponIndex) => weaponIndex switch
        {
            1 => "Pencil",
            2 => "Brush",
            3 => "PaintKnife",
            4 => "Palette",
            _ => "Pencil",
        };

        /// <summary>
        /// 讀取 Animator 目前正在播放的動畫實際長度，取代寫死的猜測時間——
        /// 同一個 Trigger 在不同武器下會接到長度不同的動畫（例如 Combo3_Brush 6.43s vs Combo1_Pencil 3.97s），
        /// 用這個才能讓每把武器都精準地播完自己的動畫再交還操作權，而不是全部套同一個保險時間。
        ///
        /// 用法：Enter() 把 cachedLength 歸零成 -1，之後每幀 Update() 呼叫本方法取得「這幀該用的持續時間」。
        /// 必須傳入「這次應該切到的 Animator state 名稱」比對——
        /// 有些武器目前還沒有對應動畫（例如調色刀/調色盤的攻擊、除了鉛筆以外的切換武器動畫），
        /// 這種情況下 Trigger 送出去但 Animator 沒有任何轉場可用，會停在原本的狀態（例如還在 Idle/Locomotion）。
        /// 如果不比對名稱、只看「有沒有在轉場」，就會誤把 Idle 或跑步 Blend Tree 的長度當成這次攻擊的長度，
        /// 導致沒動畫的武器攻擊起來反而要等更久。名稱對不上就一路用 fallback，絕不鎖定錯誤的長度。
        /// </summary>
        protected float ResolveDuration(ref float cachedLength, string expectedStateName, float fallback)
        {
            if (cachedLength < 0f)
            {
                if (Anim.IsInTransition(0)) return fallback;
                var info = Anim.GetCurrentAnimatorStateInfo(0);
                if (info.IsName(expectedStateName) && info.length > 0.01f)
                    cachedLength = info.length;
                else
                    return fallback;
            }
            return cachedLength;
        }

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

        // 依 Combat.CurrentWeaponIndex（1~4）對應到 Animator 裡的待機狀態名稱
        private static readonly string[] IdleStateNames =
        {
            null,              // 0：未使用
            "Idle_Pencil",     // 武器1 鉛筆
            "Idle_Brush",      // 武器2 水彩筆
            "Idle_PaintKnife", // 武器3 畫刀
            "Idle_Palette",    // 武器4 調色盤
        };

        public override void Enter()
        {
            SetAnimBool("IsMoving", false);
            SetAnimBool("IsSprinting", false);

            int weaponIndex = Player.Combat != null ? Player.Combat.CurrentWeaponIndex : 1;
            if (weaponIndex >= 1 && weaponIndex < IdleStateNames.Length)
                CrossFadeAnimation(IdleStateNames[weaponIndex]);

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
        private Vector3 _rollDir;

        public RollState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            _rollTimer = 0f;
            _rollDir = Player.GetRollDirection(Input.MoveInput);
            Player.SetInvincible(true);
            PlayAnimation("Roll");
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

        public override void FixedUpdate(float fdt)
            => Player.MoveRoll(_rollDir);

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
        private float _elapsed;
        private float _clipLength;
        private bool _nextInputBuffered; // 動畫中提前輸入的緩衝

        // SoftAttack_pencil=1.70s、SoftAttack_Brush=1.93s；實際長度改由 ResolveDuration 動態讀取，這只是兩者都讀不到時的保險值
        private const float FallbackDuration = 2.0f;

        public LightAttackState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            _animDone = false;
            _elapsed = 0f;
            _clipLength = -1f;
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
                _elapsed += dt;
                int weaponIndex = Player.Combat != null ? Player.Combat.CurrentWeaponIndex : 1;
                float duration = ResolveDuration(ref _clipLength, "LightAttack_" + WeaponSuffix(weaponIndex), FallbackDuration);
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
                if (_elapsed >= duration) _animDone = true;
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
            // 這次攻擊沒有接成連段、也沒有後續輸入：把緩衝清乾淨。
            // 不清的話，這一下攻擊殘留的輸入紀錄會在視窗時間內（現在拉長到 3s）
            // 跟下一次完全獨立、玩家沒打算連段的攻擊被誤判成連段序列。
            Player.ComboBuffer.Reset();
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
        private float _elapsed;
        private float _clipLength;
        private bool _nextInputBuffered;
        // HardAttack_pencil=2.80s、HardAttack_Brush=2.10s；實際長度改由 ResolveDuration 動態讀取，這只是保險值
        private const float FallbackDuration = 2.9f;

        public HeavyAttackState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            _animDone = false;
            _elapsed = 0f;
            _clipLength = -1f;
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
                _elapsed += dt;
                int weaponIndex = Player.Combat != null ? Player.Combat.CurrentWeaponIndex : 1;
                float duration = ResolveDuration(ref _clipLength, "HeavyAttack_" + WeaponSuffix(weaponIndex), FallbackDuration);
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
                if (_elapsed >= duration) _animDone = true;
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
            // 沒接成連段、也沒有後續輸入：清掉緩衝，避免殘留輸入跟下一次無關的攻擊誤判成連段。
            Player.ComboBuffer.Reset();
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
        private float _elapsed;
        private float _clipLength;
        private int _comboIndex;
        // Combo1~4 × 鉛筆/畫筆最長是 Combo3_Brush = 6.43s；實際長度改由 ResolveDuration 依當下
        // 播的是哪個 Combo{n}_武器 動態讀取，這只是完全讀不到時的保險值
        private const float FallbackDuration = 6.5f;

        public ComboAttackState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            _animDone = false;
            _elapsed = 0f;
            _clipLength = -1f;
            _comboIndex = Player.ComboBuffer.CurrentComboIndex;
            PlayAnimation($"Combo{_comboIndex}");
            Player.OnAttackAnimationEnd += HandleAnimEnd;
            Player.Combat?.StartComboAttack(_comboIndex);
        }

        public override void Update(float dt)
        {
            if (ShouldPause()) { RequestTransition(PlayerStateType.Paused); return; }
            if (Player.IsDead) { RequestTransition(PlayerStateType.Dead); return; }
            if (Input.SpecialPressed && Player.CanUseSpecial)
            { RequestTransition(PlayerStateType.SpecialSkill); return; }

            if (!_animDone)
            {
                _elapsed += dt;
                int weaponIndex = Player.Combat != null ? Player.Combat.CurrentWeaponIndex : 1;
                float duration = ResolveDuration(ref _clipLength, $"Combo{_comboIndex}_" + WeaponSuffix(weaponIndex), FallbackDuration);
                if (_elapsed >= duration) _animDone = true;
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
        private float _clipLength;
        private bool _animDone;
        private const float FallbackDuration = 5.4f; // SP_skill_pencil 實際長度 5.27s，讀不到動畫長度時的保險值
        private const float InvincibleEndPercent = 0.8f; // 前80%有無敵幀

        public SpecialSkillState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            _timer = 0f;
            _clipLength = -1f;
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
            int weaponIndex = Player.Combat != null ? Player.Combat.CurrentWeaponIndex : 1;
            float duration = ResolveDuration(ref _clipLength, "SpecialSkill_" + WeaponSuffix(weaponIndex), FallbackDuration);

            // 前80%有無敵，後20%移除（模擬末尾幾幀無無敵）
            if (_timer >= duration * InvincibleEndPercent)
                Player.SetInvincible(false);

            if (!_animDone && _timer >= duration)
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
        private float _elapsed;
        private float _clipLength;
        private int _weaponIndex;
        // ChangeWeapon_pencil 實際長度 2.10s；其他武器目前沒有切換動畫，讀不到時走這個保險時間
        private const float FallbackDuration = 2.2f;

        public WeaponSwitchState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            _animDone = false;
            _elapsed = 0f;
            _clipLength = -1f;
            _weaponIndex = Player.PendingWeaponIndex;
            PlayAnimation($"WeaponSwitch_{_weaponIndex}");
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
                _elapsed += dt;
                float duration = ResolveDuration(ref _clipLength, "WeaponSwitch_" + WeaponSuffix(_weaponIndex), FallbackDuration);
                if (_elapsed >= duration) _animDone = true;
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

                // 用 ForceTransition 而非 RequestTransition：Paused 優先權是 0（最高），
                // 但 _resumeState 可能是 Roll/攻擊/連段/跳躍/特殊招式，優先權數字是 2~3，
                // 一般的優先權檢查（目標數字 <= 目前數字）在這裡永遠不通過，會讓
                // RequestTransition 靜默失敗，角色卡在 Paused、timeScale 永遠回不去 1。
                // 這裡是「離開系統層暫停、恢復先前動作」，不該被戰鬥打斷規則擋下。
                Machine.ForceTransition(_resumeState);
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