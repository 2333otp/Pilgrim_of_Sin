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
        {
            Anim.SetTrigger(triggerName);
            Player.Feedback?.NotifyTrigger(triggerName); // 音效 / 配音 / 特效（沒設定就什麼都不做）
        }

        /// <summary>設定 Animator Bool 參數。</summary>
        protected void SetAnimBool(string paramName, bool value)
            => Anim.SetBool(paramName, value);

        // 會經由 Any State 轉場消耗的一次性 Trigger（不含 Death，死亡不可被清掉）
        private static readonly string[] ClearableTriggers =
        {
            "LightAttack", "HeavyAttack", "Combo1", "Combo2", "Combo3", "Combo4",
            "SpecialSkill", "Roll", "Jump", "Fall", "Damaged",
            "WeaponSwitch_1", "WeaponSwitch_2", "WeaponSwitch_3", "WeaponSwitch_4",
        };

        /// <summary>
        /// 清掉所有殘留的 Animator Trigger。Any State 轉場（InterruptionSource=None）在任何轉場進行中
        /// 都會被擋下，Trigger 因此留在 Animator 裡沒被消耗；之後某個無關時刻轉場結束，殘留 Trigger
        /// 才突然觸發，把動畫拉進早已不屬於當前狀態的姿勢（例如被打斷的攻擊/受傷動畫），而這些
        /// 動畫都沒有「出口」轉場，Animator 就卡在該姿勢、程式狀態卻已回到 Idle/Walk。
        /// 另外，沒有對應轉場的武器（例如缺動畫的 Trigger）也會一直殘留。
        /// 受傷打斷、離開技能時呼叫。
        /// </summary>
        protected void ClearPendingTriggers()
        {
            foreach (var t in ClearableTriggers) Anim.ResetTrigger(t);
        }

        /// <summary>
        /// 攻擊動畫改用「狀態名稱 + 武器後綴」直接 CrossFade，不走 Trigger + Any State。
        /// 原因：Any State → 攻擊的轉場設定為 Can Transition To Self = false、Interruption = None，
        /// 攻擊播到一半（取消時間點）要接「同一種攻擊」時，Trigger 不會重播，還會殘留在 Animator 裡；
        /// CrossFade 直接指定狀態，同一個攻擊也能從頭重播。音效/特效走 NotifyState，
        /// 名稱與原本 NotifyTrigger 換算出來的一致（例：LightAttack → LightAttack_Pencil）。
        /// Animator 裡沒有這個 state 時（例如之後新增武器還沒做動畫）退回原本的 Trigger 寫法。
        /// </summary>
        protected void PlayAttackAnimation(string actionPrefix, float crossFadeSeconds = 0.1f)
        {
            int weaponIndex = Player.Combat != null ? Player.Combat.CurrentWeaponIndex : 1;
            string stateName = actionPrefix + "_" + WeaponSuffix(weaponIndex);
            if (!Anim.HasState(0, Animator.StringToHash(stateName)))
            {
                PlayAnimation(actionPrefix);
                return;
            }
            ClearPendingTriggers();
            Anim.CrossFadeInFixedTime(stateName, crossFadeSeconds, 0);
            Player.Feedback?.NotifyState(stateName);
        }

        protected enum AttackCancel
        {
            None,        // 還不能取消，或沒有要取消的輸入：照常播下去
            NextAttack,  // 已到取消時間點且有下一個攻擊輸入：呼叫端立刻接下一招
            Exited,      // 已經切到翻滾 / 移動，呼叫端直接 return
        }

        /// <summary>
        /// 攻擊取消時間點的共用判斷（輕攻擊、重攻擊、連段共用）。
        /// 播到 cancelRatio 之後：先看翻滾（翻滾取消收招），再看下一個攻擊；
        /// 播到 PlayerController.MoveCancelRatio 之後，推搖桿就直接離開攻擊。
        /// 翻滾優先級（3）比攻擊（2）低，一般 RequestTransition 會被擋，所以只在取消時間點之後用 ForceTransition 放行。
        /// </summary>
        protected AttackCancel CheckAttackCancel(float elapsed, float duration, float cancelRatio,
                                                 bool attackBuffered, bool rollBuffered)
        {
            if (!Player.EnableAttackCancel || duration <= 0f) return AttackCancel.None;

            float progress = elapsed / duration;
            if (progress < cancelRatio) return AttackCancel.None;

            if (rollBuffered)
            {
                Player.ComboBuffer.Reset();
                Machine.ForceTransition(PlayerStateType.Roll);
                return AttackCancel.Exited;
            }
            if (attackBuffered) return AttackCancel.NextAttack;

            if (progress >= Player.MoveCancelRatio && Input.MoveInput.sqrMagnitude > 0.01f)
            {
                Player.ComboBuffer.Reset();
                RequestIdleOrWalk();
                return AttackCancel.Exited;
            }
            return AttackCancel.None;
        }

        /// <summary>直接以狀態名稱切換 Animator（不經過 Transition 條件，適合依武器等資料切換待機姿勢）。</summary>
        protected void CrossFadeAnimation(string stateName, float duration = 0.15f)
        {
            Anim.CrossFade(stateName, duration);
            Player.Feedback?.NotifyState(stateName);
        }

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
            // PlayerProfile 有放這個 state 的 Clip：直接用 Clip 長度（換不同長度的動畫會自動對齊）。
            // 沒放就走下面原本的邏輯，Profile 可自訂保險時間。
            var feedback = Player.Feedback;
            if (feedback != null)
            {
                if (feedback.TryGetDuration(expectedStateName, out float clipDuration)) return clipDuration;
                fallback = feedback.GetFallback(expectedStateName, fallback);
            }

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
        /// ESC 暫停偵測：PlayerInput.PausePressed 或 Keyboard/Gamepad 直讀裝置備援。
        /// 手把直讀是必要的：手把第一次被摸到那一下，PlayerInput 的 currentControlScheme
        /// 還沒切過去 "Gamepad"（配對跟切換方案有時間差），Action 的 SendMessage
        /// 會用舊方案判斷而被吃掉，PausePressed 抓不到那一下，要直接讀裝置原始狀態備援。
        /// </summary>
        protected bool ShouldPause()
            => Input.PausePressed
               || Input.PauseFallbackKeyPressed
               || Input.PauseFallbackStartPressed;

        /// <summary>
        /// 攻擊/技能/切換武器結束時用這個決定回哪個狀態，取代直接 RequestTransition(Idle)。
        /// 玩家還按著移動鍵的話直接接回 Walk，不要先繞路回 Idle——IdleState.Enter() 會
        /// CrossFade 到 Idle 姿勢（0.15s），這段 Crossfade 進行中 Any State 轉場無法打斷
        /// （InterruptionSource=None），但 WalkState 已經開始用 Move() 全速位移，
        /// 表現就是「攻擊完回到跑步會平移一下才切正確動畫」。
        /// </summary>
        protected void RequestIdleOrWalk()
        {
            RequestTransition(Input.MoveInput.sqrMagnitude > 0.01f
                ? PlayerStateType.Walk
                : PlayerStateType.Idle);
        }

        /// <summary>
        /// 地面攻擊入口（Idle / Walk / Sprint 用）：目前武器有勾選出招表就走 MoveAttack，
        /// 否則維持舊的輕／重攻擊狀態。空中攻擊（Jump / Fall）不走這裡，仍用舊狀態。
        /// </summary>
        protected void RequestGroundAttack(bool heavy)
        {
            if (Player.CurrentComboSet != null)
            {
                Player.PendingAttackHeavy = heavy;
                RequestTransition(PlayerStateType.MoveAttack);
                return;
            }
            RequestTransition(heavy ? PlayerStateType.HeavyAttack : PlayerStateType.LightAttack);
        }
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
            ClearPendingTriggers(); // 見 ClearPendingTriggers：避免殘留 Trigger 在待機中途把動畫拉走

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
            if (Input.LightAttackPressed) { RequestGroundAttack(false); return; }
            if (Input.HeavyAttackPressed) { RequestGroundAttack(true); return; }
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

            // 只靠 IsMoving 讓 Any State 轉場切到 Locomotion 不夠穩：Any State 轉場
            // （InterruptionSource=None）在任何轉場進行中都會被擋下，例如技能/攻擊結束後
            // Idle 的 CrossFade 或動畫收尾混合。此時 Walk 已經用 Move() 全速位移，
            // 腿卻還沒動，表現就是「先滑行一小段才開始走路」。這裡跟 IdleState 一樣主動
            // CrossFade，直接打斷進行中的轉場；已經在 Locomotion 就不重播，避免動畫被重置。
            int weaponIndex = Player.Combat != null ? Player.Combat.CurrentWeaponIndex : 1;
            string locomotion = "Locomotion_" + WeaponSuffix(weaponIndex);
            var info = Anim.GetCurrentAnimatorStateInfo(0);
            if (Anim.IsInTransition(0) || !info.IsName(locomotion))
                CrossFadeAnimation(locomotion, 0.1f);
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
            if (Input.LightAttackPressed) { RequestGroundAttack(false); return; }
            if (Input.HeavyAttackPressed) { RequestGroundAttack(true); return; }

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
            if (Input.LightAttackPressed) { RequestGroundAttack(false); return; }
            if (Input.HeavyAttackPressed) { RequestGroundAttack(true); return; }

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
        private float _windup;  // 按下跳躍後、物理起跳前的蓄力時間
        private bool _launched;
        private const float MinAirTime = 0.15f; // 至少滯空這麼久才判斷落地

        public JumpState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            _airTime = 0f;
            _windup = 0f;
            _launched = false;
            // 見 RollState.Enter() 註解：IsMoving 還是 true 會讓 Any State → Locomotion 切掉跳躍動畫。
            SetAnimBool("IsMoving", false);
            SetAnimBool("IsSprinting", false);
            PlayAnimation("Jump");
            // 物理起跳延到動畫蓄力（深蹲）結束的影格，見 PlayerController._jumpLaunchDelay。
            if (Player.JumpLaunchDelay <= 0f) Launch();
        }

        private void Launch()
        {
            _launched = true;
            Player.ApplyJumpForce();
        }

        public override void Update(float dt)
        {
            if (_launched) _airTime += dt;
            else
            {
                _windup += dt;
                if (_windup >= Player.JumpLaunchDelay) Launch();
            }

            if (ShouldPause()) { RequestTransition(PlayerStateType.Paused); return; }
            if (Player.IsDead) { RequestTransition(PlayerStateType.Dead); return; }
            if (Input.SpecialPressed && Player.CanUseSpecial)
            { RequestTransition(PlayerStateType.SpecialSkill); return; }
            if (Input.LightAttackPressed) { RequestTransition(PlayerStateType.LightAttack); return; }
            if (Input.HeavyAttackPressed) { RequestTransition(PlayerStateType.HeavyAttack); return; }

            // 還在蓄力（沒離地）或 MinAirTime 內不判斷落地，避免起跳第一幀就被拉回 Idle
            if (!_launched || _airTime < MinAirTime) return;

            if (Player.IsFalling) { RequestTransition(PlayerStateType.Fall); return; }
            // 還在上升不算落地（剛起跳腳底仍貼著地面）
            if (Player.IsLanded && Player.VerticalVelocity <= 0.5f) { RequestTransition(PlayerStateType.Idle); return; } // 矮跳直接落地
        }

        public override void FixedUpdate(float fdt)
        {
            if (_launched) Player.MoveAerial(Input.MoveInput);
        }
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
            if (Player.IsLanded) { RequestTransition(PlayerStateType.Idle); return; }
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
            // 見 LightAttackState.Enter() 註解：從 Walk/Sprint 進來時 IsMoving 還是 true，
            // Any State → Locomotion 轉場會在 Roll 轉場結束後立刻成立，把翻滾動畫切掉。
            SetAnimBool("IsMoving", false);
            SetAnimBool("IsSprinting", false);
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
        private bool _rollBuffered;      // 動畫中按了翻滾，到取消時間點才執行

        // SoftAttack_pencil=1.70s、SoftAttack_Brush=1.93s；實際長度改由 ResolveDuration 動態讀取，這只是兩者都讀不到時的保險值
        private const float FallbackDuration = 2.0f;

        public LightAttackState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            _animDone = false;
            _elapsed = 0f;
            _clipLength = -1f;
            _nextInputBuffered = false;
            _rollBuffered = false;
            // 移動中攻擊會卡住的根因：Animator 有「Any State -> 移動 BlendTree」轉場，條件只看
            // IsMoving==true，沒有 Exit Time、每幀重判。若不在這裡關掉 IsMoving，攻擊動畫播下一幀
            // 就被拉回 Locomotion，ResolveDuration() 永遠比對不到攻擊動畫名稱，只能用保底時間鎖住
            // 操作，表現就是移動時攻擊卡住不動、要等保底時間到才恢復。
            SetAnimBool("IsMoving", false);
            SetAnimBool("IsSprinting", false);
            PlayAttackAnimation("LightAttack");
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
                if (Input.RollPressed) _rollBuffered = true;

                switch (CheckAttackCancel(_elapsed, duration, Player.LightCancelRatio, _nextInputBuffered, _rollBuffered))
                {
                    case AttackCancel.Exited: return;
                    case AttackCancel.NextAttack: _animDone = true; break; // 提前進入下面的連段判斷
                }
                if (_elapsed >= duration) _animDone = true;
                if (!_animDone) return;
            }

            // 動畫結束（或到了取消時間點且有下一個輸入）：判斷是否觸發連段
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
            RequestIdleOrWalk();
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
        private bool _rollBuffered;
        // HardAttack_pencil=2.80s、HardAttack_Brush=2.10s；實際長度改由 ResolveDuration 動態讀取，這只是保險值
        private const float FallbackDuration = 2.9f;

        public HeavyAttackState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            _animDone = false;
            _elapsed = 0f;
            _clipLength = -1f;
            _nextInputBuffered = false;
            _rollBuffered = false;
            // 見 LightAttackState.Enter() 註解：移動中攻擊卡住的根因同一套，這裡同樣要關掉。
            SetAnimBool("IsMoving", false);
            SetAnimBool("IsSprinting", false);
            PlayAttackAnimation("HeavyAttack");
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
                if (Input.RollPressed) _rollBuffered = true;

                switch (CheckAttackCancel(_elapsed, duration, Player.HeavyCancelRatio, _nextInputBuffered, _rollBuffered))
                {
                    case AttackCancel.Exited: return;
                    case AttackCancel.NextAttack: _animDone = true; break;
                }
                if (_elapsed >= duration) _animDone = true;
                if (!_animDone) return;
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
            RequestIdleOrWalk();
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
        // 連段收招後提前按下的下一個攻擊（開始新的一輪），以及翻滾取消
        private bool _nextAttackBuffered;
        private ComboBuffer.AttackInput _nextAttack;
        private bool _rollBuffered;
        // Combo1~4 × 鉛筆/畫筆最長是 Combo3_Brush = 6.43s；實際長度改由 ResolveDuration 依當下
        // 播的是哪個 Combo{n}_武器 動態讀取，這只是完全讀不到時的保險值
        private const float FallbackDuration = 6.5f;

        public ComboAttackState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            _animDone = false;
            _elapsed = 0f;
            _clipLength = -1f;
            _nextAttackBuffered = false;
            _rollBuffered = false;
            _comboIndex = Player.ComboBuffer.CurrentComboIndex;
            // 見 LightAttackState.Enter() 註解：移動中攻擊卡住的根因同一套，這裡同樣要關掉。
            SetAnimBool("IsMoving", false);
            SetAnimBool("IsSprinting", false);
            PlayAttackAnimation($"Combo{_comboIndex}");
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

                // 連段播放中提前按的攻擊：記住最後一個，取消時間點到了就從它開始新的一輪
                if (Input.HeavyAttackPressed) { _nextAttack = ComboBuffer.AttackInput.Heavy; _nextAttackBuffered = true; }
                else if (Input.LightAttackPressed) { _nextAttack = ComboBuffer.AttackInput.Light; _nextAttackBuffered = true; }
                if (Input.RollPressed) _rollBuffered = true;

                switch (CheckAttackCancel(_elapsed, duration, Player.ComboCancelRatio, _nextAttackBuffered, _rollBuffered))
                {
                    case AttackCancel.Exited: return;
                    case AttackCancel.NextAttack: _animDone = true; break;
                }
                if (_elapsed >= duration) _animDone = true;
                if (!_animDone) return;
            }

            Player.ComboBuffer.Reset();
            if (_nextAttackBuffered)
            {
                // 連段結束後接新的一輪：交給輕/重攻擊狀態，它們的 Enter() 會把這一下記進連段緩衝
                RequestTransition(_nextAttack == ComboBuffer.AttackInput.Heavy
                    ? PlayerStateType.HeavyAttack
                    : PlayerStateType.LightAttack);
                return;
            }
            RequestIdleOrWalk();
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
    //  MoveAttackState（出招表：無雙式連段研究）
    //
    //  依目前武器 WeaponActions 的「出招表」出招：
    //    ⬜ 連按：N1 → N2 → N3 → N4（N4 之後再按 ⬜ 從 N1 重新開始）
    //    🔺：直接按 = C1；第 k 下一般攻擊後按 = C(k+1)；強攻擊之後再按 🔺 = C1
    //  惡魔獵人分支（有放動畫才會啟用）：
    //    停頓連打：在第 pauseAfterNormal 下一般攻擊（預設 N2）的「取消時間點 → 播完 + 停頓寬限」之間按 ⬜。
    //              取消時間點之前就按 ⬜ = 一般連段的下一下；寬限也過了才按 = 從 N1 重新開始。
    //    蓄力：🔺 按住超過 chargeHoldSeconds 出蓄力；更早放開出原本該出的 C。
    //          按住的時間從按下那一刻算（預輸入時就開始計），所以在連段中提早按住不會多等。
    //    停頓連打 / 蓄力之後再按 ⬜ = N1、🔺 = C1。
    //  每一招播的就是出招表裡放的那段動畫（AttackClipPlayer 疊在 Animator 上播），
    //  進度直接讀「這段動畫實際播到哪」，所以動畫換長換短都不會被提早切掉或卡住。
    //  播到這一招的取消時間點之後：已按下的下一招立刻接上 / 可翻滾取消；
    //  播到 MoveCancelRatio 之後推搖桿可直接移動。整段播完回 Idle / Walk。
    // ════════════════════════════════════════════════════════════════
    public class MoveAttackState : PlayerState
    {
        public override PlayerStateType StateType => PlayerStateType.MoveAttack;

        /// <summary>目前這一招屬於出招表的哪一排。</summary>
        public enum MoveKind { Normal, Strong, Pause, Hold }

        private ComboMoveSet _set;
        private ComboMoveEntry _move;
        private MoveKind _kind;
        private int _index;            // 一般攻擊：0 = N1；強攻擊：0 = C1；分支招固定 0
        private bool _bufferedLight;
        private bool _bufferedHeavy;
        private bool _bufferedPause;
        private bool _bufferedRoll;
        private float _elapsed;        // 這一招已播的動畫時間（保險計時）
        private float _graceTimer;     // 停頓寬限：這一招播完之後又等了多久

        // 命中區間：每一下各開一次攻擊判定
        private float _moveDamage;     // 整招傷害
        private int _activeWindow = -1;

        // 🔺 長按判斷
        private float _heavyPressTime;
        private bool _heavyReleased;   // 這次按下的 🔺 已經放開過
        private bool _awaitingHold;    // 正在等「放開 → C」或「按滿 → 蓄力」
        private int _pendingStrongIndex;

        // ── 唯讀狀態（給 F1 開發者面板顯示連段進度用，不影響邏輯） ──
        public ComboMoveEntry CurrentMove => _move;
        public MoveKind Kind => _kind;
        public bool IsCharge => _kind == MoveKind.Strong;
        public int MoveIndex => _index;
        /// <summary>目前這一招播到動畫的比例（0~1，停頓寬限中會超過 1）。</summary>
        public float Progress { get; private set; }
        public bool BufferedLight => _bufferedLight;
        public bool BufferedHeavy => _bufferedHeavy;
        public bool BufferedPause => _bufferedPause;
        public bool BufferedRoll => _bufferedRoll;
        public bool AwaitingHold => _awaitingHold;
        public int PendingStrongIndex => _pendingStrongIndex;
        /// <summary>目前開著的命中區間（-1 = 沒有；這一招沒設命中區間時整招都算 0）。</summary>
        public int ActiveHitWindow => _activeWindow;
        /// <summary>目前這一招之後可以接停頓連打（取消時間點之後按 ⬜ 就是停頓連打）。</summary>
        public bool PauseEligible =>
            _move != null && _set != null && _kind == MoveKind.Normal &&
            _index == _set.pauseAfterNormal - 1 && _set.pauseCombo != null && _set.pauseCombo.IsValid;
        public string SlotName => _kind switch
        {
            MoveKind.Normal => "N" + (_index + 1),
            MoveKind.Strong => "C" + (_index + 1),
            MoveKind.Pause => "停頓連打",
            _ => "蓄力",
        };

        public MoveAttackState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            _move = null;
            _awaitingHold = false;
            _set = Player.CurrentComboSet;
            if (_set == null) return;   // 出招表剛好被關掉：Update 會直接回 Idle/Walk

            // 見 LightAttackState.Enter() 註解：IsMoving 不關掉，Any State → Locomotion 會把底下的 Animator 拉去跑步。
            SetAnimBool("IsMoving", false);
            SetAnimBool("IsSprinting", false);
            ClearPendingTriggers();
            // 底下的 Animator 先回待機姿勢：攻擊結束淡出時銜接待機，而不是露出進攻擊前的跑步姿勢
            int weaponIndex = Player.Combat != null ? Player.Combat.CurrentWeaponIndex : 1;
            string idle = "Idle_" + WeaponSuffix(weaponIndex);
            if (Anim.HasState(0, Animator.StringToHash(idle))) Anim.CrossFadeInFixedTime(idle, 0.1f, 0);

            Player.ComboBuffer.Reset();
            if (Player.PendingAttackHeavy)
            {
                BeginHeavyTracking();
                QueueStrong(0);   // 有蓄力動畫時會先等一下看是不是長按
            }
            else
            {
                StartNormal(0);
            }
        }

        public override void Update(float dt)
        {
            if (ShouldPause()) { RequestTransition(PlayerStateType.Paused); return; }
            if (Player.IsDead) { RequestTransition(PlayerStateType.Dead); return; }
            if (_move == null && !_awaitingHold) { RequestIdleOrWalk(); return; }
            if (Input.SpecialPressed && Player.CanUseSpecial)
            { RequestTransition(PlayerStateType.SpecialSkill); return; }

            if (!Input.HeavyAttackHeld) _heavyReleased = true;

            // 正在判斷 🔺 是短按還是長按：上一招照常播（播完就停在最後一格），等判斷完才出下一招
            if (_awaitingHold)
            {
                ResolveHold();
                return;
            }

            // 進度以「動畫實際播到哪」為準；另外自己用遊戲時間計一份，取較大者當保險——
            // 播放器萬一沒在走（例如 Play 中途重新編譯），也保證會播完離開，不會卡死在攻擊裡。
            _elapsed += Time.deltaTime * Mathf.Max(0.1f, _move.speed);
            float length = _move.clip.length;
            float progress = length > 0f
                ? Mathf.Max(Player.ClipPlayer.CurrentClipTime, _elapsed) / length
                : 1f;
            Progress = progress;
            UpdateHitWindows(progress);
            bool pauseEligible = PauseEligible;

            // 預輸入：只有播到「接收開始」之後按的鍵才算，而且只認第一個（⬜ / 🔺 / 翻滾 都算一個指令）。
            // 接收開始之前按的直接忽略；收到第一個之後，這一招剩下的時間再按什麼都不改。
            // 可以接停頓連打的那一招：取消時間點之後才按的 ⬜ 算停頓連打。
            bool alreadyBuffered = _bufferedLight || _bufferedHeavy || _bufferedPause || _bufferedRoll;
            if (!alreadyBuffered && progress >= _move.inputStartRatio)
            {
                if (Input.RollPressed) _bufferedRoll = true;
                else if (Input.HeavyAttackPressed) { _bufferedHeavy = true; BeginHeavyTracking(); }
                else if (Input.LightAttackPressed)
                {
                    if (pauseEligible && progress >= _move.cancelRatio) _bufferedPause = true;
                    else _bufferedLight = true;
                }
            }

            bool canCancel = Player.EnableAttackCancel ? progress >= _move.cancelRatio : progress >= 1f;
            if (canCancel)
            {
                if (_bufferedRoll)
                {
                    Machine.ForceTransition(PlayerStateType.Roll);   // 翻滾優先級較低，只在取消點之後強制放行
                    return;
                }
                if (_bufferedPause)
                {
                    if (!StartPause()) StartNormal(0);
                    return;
                }
                if (_bufferedLight)
                {
                    // 一般攻擊接下一下；強攻擊／分支招之後或 N4 之後從 N1 重新開始
                    if (_kind != MoveKind.Normal || !StartNormal(_index + 1)) StartNormal(0);
                    return;
                }
                if (_bufferedHeavy)
                {
                    // 第 k 下一般攻擊後 → C(k+1)；強攻擊／分支招之後、或停頓寬限中（已經播完）→ C1
                    int target = _kind == MoveKind.Normal && progress < 1f ? _index + 1 : 0;
                    QueueStrong(target);
                    return;
                }
                if (Player.EnableAttackCancel && progress >= Player.MoveCancelRatio && Input.MoveInput.sqrMagnitude > 0.01f)
                {
                    RequestIdleOrWalk();
                    return;
                }
            }

            if (progress >= 1f)
            {
                // 可以接停頓連打：播完後再留一小段寬限，讓「停半拍」再按的 ⬜ 還接得到
                if (pauseEligible)
                {
                    _graceTimer += dt;
                    if (_graceTimer <= _set.pauseGraceSeconds) return;
                }
                RequestIdleOrWalk();
            }
        }

        public override void Exit()
        {
            Player.ClipPlayer.Stop(0.15f);
            Player.Combat?.EndAttack();
            _move = null;
            _awaitingHold = false;
        }

        // ── 🔺 長按判斷 ────────────────────────────────────────────────
        private void BeginHeavyTracking()
        {
            _heavyPressTime = Time.time;
            _heavyReleased = !Input.HeavyAttackHeld;
        }

        private bool HasHoldMove => _set != null && _set.chargeHold != null && _set.chargeHold.IsValid;

        private bool HeldLongEnough => !_heavyReleased && Time.time - _heavyPressTime >= _set.chargeHoldSeconds;

        /// <summary>要出強攻擊 C(target)：有蓄力動畫就先判斷是不是長按，沒有就立刻出。</summary>
        private void QueueStrong(int target)
        {
            _bufferedHeavy = false;
            if (!HasHoldMove || _heavyReleased) { StartStrongOrFallback(target); return; }
            if (HeldLongEnough) { StartHold(); return; }

            _awaitingHold = true;
            _pendingStrongIndex = target;
        }

        private void ResolveHold()
        {
            if (_heavyReleased) { _awaitingHold = false; StartStrongOrFallback(_pendingStrongIndex); }
            else if (HeldLongEnough) { _awaitingHold = false; StartHold(); }
        }

        /// <summary>出 C(target)；沒有就出 C1；連 C1 都沒有而且手上沒招在播，至少出 N1。</summary>
        private void StartStrongOrFallback(int target)
        {
            if (StartStrong(target) || StartStrong(0)) return;
            if (_move == null) StartNormal(0);
        }

        // ── 出招 ───────────────────────────────────────────────────────
        private bool StartNormal(int index) => Begin(_set?.GetNormal(index), MoveKind.Normal, index);
        private bool StartStrong(int index) => Begin(_set?.GetCharge(index), MoveKind.Strong, index);
        private bool StartPause() => Begin(_set?.pauseCombo, MoveKind.Pause, 0);
        private bool StartHold() => Begin(_set?.chargeHold, MoveKind.Hold, 0);

        private bool Begin(ComboMoveEntry move, MoveKind kind, int index)
        {
            if (move == null || !move.IsValid) return false;

            _move = move;
            _kind = kind;
            _index = index;
            _elapsed = 0f;
            _graceTimer = 0f;
            Progress = 0f;
            _bufferedLight = _bufferedHeavy = _bufferedPause = _bufferedRoll = false;

            Player.ClipPlayer.Play(move.clip, move.speed, 0.1f);
            Player.Feedback?.FireMove(move);

            _activeWindow = -1;
            if (Player.Combat != null)
            {
                Player.Combat.EndAttack();
                _moveDamage = move.damage > 0f ? move.damage
                    : kind == MoveKind.Pause ? Player.Combat.GetDefaultBranchMoveDamage(false)
                    : kind == MoveKind.Hold ? Player.Combat.GetDefaultBranchMoveDamage(true)
                    : Player.Combat.GetDefaultMoveDamage(kind == MoveKind.Strong, index);
                // 沒設命中區間：整招只算一下（舊行為），一出招就開判定
                if (!move.HasHitWindows)
                {
                    Player.Combat.StartMoveAttack(_moveDamage, _set.ResolveHitbox(null, move.customHitbox, move.hitbox), move.Duration);
                    _activeWindow = 0;
                }
            }
            return true;
        }

        /// <summary>
        /// 依動畫進度開關攻擊判定：進入一個命中區間就重新開一次（命中記錄清空，同一個敵人可以再被打一次），
        /// 離開區間就關掉。區間沒填傷害時平分整招傷害。
        /// </summary>
        private void UpdateHitWindows(float progress)
        {
            if (_move == null || !_move.HasHitWindows || Player.Combat == null) return;
            int w = _move.GetWindowAt(progress);
            if (w == _activeWindow) return;
            _activeWindow = w;
            if (w >= 0)
            {
                var window = _move.hitWindows[w];
                var shape = _set.ResolveHitbox(window, _move.customHitbox, _move.hitbox);
                float seconds = (window.end - window.start) * _move.Duration;
                Player.Combat.StartMoveAttack(_move.GetWindowDamage(w, _moveDamage), shape, seconds);
            }
            else Player.Combat.EndAttack();
        }
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

        // 命中區間（出招表的「秘劍」欄）：每一下各開一次判定；沒設就整招一下（舊行為）
        private System.Collections.Generic.List<HitWindow> _windows;
        private int _activeWindow = -1;

        /// <summary>給 F1 面板：秘劍有幾下、目前第幾下（-1 = 判定關著）、播到哪。</summary>
        public int HitCount => _windows != null ? _windows.Count : 0;
        public int ActiveHitWindow => _activeWindow;
        public float Progress { get; private set; }

        public SpecialSkillState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
        {
            _timer = 0f;
            _clipLength = -1f;
            _animDone = false;
            // 見 LightAttackState.Enter() 註解：移動中攻擊卡住的根因同一套，這裡同樣要關掉。
            SetAnimBool("IsMoving", false);
            SetAnimBool("IsSprinting", false);
            Input.ConsumeSpecial();
            Player.SetInvincible(true);
            PlayAnimation("SpecialSkill");
            Player.OnSpecialSkillAnimationEnd += HandleAnimEnd;

            int weapon = Player.Combat != null ? Player.Combat.CurrentWeaponIndex : 1;
            _set = Player.Feedback?.GetComboSet(weapon);
            var windows = _set?.specialHitWindows;
            _windows = windows != null && windows.Count > 0 ? windows : null;
            _activeWindow = -1;
            Progress = 0f;
            if (_windows == null)
            {
                // 沒設命中區間：整招一下（舊行為）；有設判定形狀就用形狀
                var shape = _set?.ResolveHitbox(null, _set.specialCustomHitbox, _set.specialHitbox);
                if (shape == null) Player.Combat?.StartSpecialAttack();
                else Player.Combat?.StartMoveAttack(Player.Combat.SpecialDamage, shape, FallbackDuration);
            }
            else Player.Combat?.EndAttack();
        }

        private ComboMoveSet _set;

        public override void Update(float dt)
        {
            if (ShouldPause()) { RequestTransition(PlayerStateType.Paused); return; }
            if (Player.IsDead) { RequestTransition(PlayerStateType.Dead); return; }

            _timer += dt;
            int weaponIndex = Player.Combat != null ? Player.Combat.CurrentWeaponIndex : 1;
            float duration = ResolveDuration(ref _clipLength, "SpecialSkill_" + WeaponSuffix(weaponIndex), FallbackDuration);
            Progress = duration > 0f ? _timer / duration : 1f;
            UpdateHitWindows(Progress, duration);

            // 前80%有無敵，後20%移除（模擬末尾幾幀無無敵）。
            // 無敵拿掉之後被打會扣血，但不會被打斷：SpecialSkill 不在 PlayerStateMachine.DamagableStates 白名單，
            // 被打時進不了 Damaged（霸體），秘劍一定會放完；只有血量歸零會直接死亡。
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
            Anim.ResetTrigger("SpecialSkill"); // 該武器沒有對應轉場時 Trigger 會一直殘留
            Player.Combat?.EndAttack();
            Player.StartSpecialSkillCooldown();
        }

        private void HandleAnimEnd() => _animDone = true;

        /// <summary>進入一個命中區間就重新開一次判定（每個敵人每一下各打一次），離開就關掉。</summary>
        private void UpdateHitWindows(float progress, float duration)
        {
            if (_windows == null || Player.Combat == null) return;
            int w = HitWindow.IndexAt(_windows, progress);
            if (w == _activeWindow) return;
            _activeWindow = w;
            if (w >= 0)
            {
                var window = _windows[w];
                var shape = _set?.ResolveHitbox(window, _set.specialCustomHitbox, _set.specialHitbox);
                Player.Combat.StartMoveAttack(HitWindow.DamageAt(_windows, w, Player.Combat.SpecialDamage), shape,
                                              (window.end - window.start) * duration);
            }
            else Player.Combat.EndAttack();
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  SpecialSkillCooldownState
    // ════════════════════════════════════════════════════════════════
    public class SpecialSkillCooldownState : PlayerState
    {
        public override PlayerStateType StateType => PlayerStateType.SpecialSkillCooldown;

        public SpecialSkillCooldownState(PlayerController p, PlayerStateMachine m) : base(p, m) { }

        public override void Enter()
            => RequestIdleOrWalk(); // CD 由 PlayerController 計時，這裡只負責回 Idle/Walk
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
            // 見 LightAttackState.Enter() 註解：移動中攻擊卡住的根因同一套，這裡同樣要關掉。
            SetAnimBool("IsMoving", false);
            SetAnimBool("IsSprinting", false);
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
            RequestIdleOrWalk();
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
            // 見 LightAttackState.Enter() 註解：IsMoving 還是 true 會讓 Any State → Locomotion 把受傷動畫切掉。
            SetAnimBool("IsMoving", false);
            SetAnimBool("IsSprinting", false);
            // 被打斷的攻擊/技能殘留的 Trigger 一併清掉，之後才不會冒出來把動畫拉走。
            ClearPendingTriggers();
            // 不用 Trigger：Any State 轉場在任何轉場進行中（Idle 的 CrossFade、技能收尾混合）都會被擋下，
            // Trigger 會延遲到轉場結束才觸發，甚至硬直結束都還沒播到受傷動畫。CrossFade 直接打斷。
            int weaponIndex = Player.Combat != null ? Player.Combat.CurrentWeaponIndex : 1;
            CrossFadeAnimation("Damaged_" + WeaponSuffix(weaponIndex), 0.05f);
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
            // 手把 Options（start）與鍵盤 Esc 分開處理：
            //  · Options：操作說明頁／確認框不作用（只有 ✕ 能退）；其他情況直接關閉整個選單。
            //  · Esc：維持「先退一層子面板，沒有才恢復遊戲」（鍵盤沒有 ✕）。
            bool startPressed = Input.PauseFallbackStartPressed;
            bool keyboardPause = Input.PauseFallbackKeyPressed
                                 || (Input.PausePressed && !startPressed);

            if (startPressed && PilgrimOfSin.PauseMenuUI.Instance != null &&
                PilgrimOfSin.PauseMenuUI.Instance.IgnoreGamepadStartNow)
                return;

            if (startPressed || keyboardPause)
            {
                if (!startPressed && PilgrimOfSin.PauseMenuUI.Instance != null &&
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
                && state != PlayerStateType.SpecialSkillCooldown
                && state != PlayerStateType.MoveAttack;   // 出招表：恢復時重進會自動再出一招，改回 Idle
        }
    }

}