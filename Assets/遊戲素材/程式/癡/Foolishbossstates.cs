using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    // ════════════════════════════════════════════════════════════════
    //  FoolishBossState  抽象基底
    // ════════════════════════════════════════════════════════════════
    public abstract class FoolishBossState
    {
        protected FoolishBossController Boss { get; }
        protected FoolishBossStateMachine Machine { get; }
        protected Animator Anim => Boss.Animator;

        public abstract FoolishBossStateType StateType { get; }

        protected FoolishBossState(FoolishBossController boss, FoolishBossStateMachine machine)
        {
            Boss = boss;
            Machine = machine;
        }

        public virtual void Enter() { }
        public virtual void Update(float dt) { }
        public virtual void FixedUpdate(float dt) { }
        public virtual void Exit() { }

        protected void Go(FoolishBossStateType next) => Machine.Request(next);
        protected void ForceGo(FoolishBossStateType next) => Machine.Force(next);
        protected FoolishBossProfile Profile => Boss.Profile;

        // 癡還沒綁 Animator Controller 時直接略過（PlayAnimTrigger 內會檢查）
        protected void Trigger(string name) => Boss.PlayAnimTrigger(name);
    }

    // ════════════════════════════════════════════════════════════════
    //  IdleState  待機決策
    // ════════════════════════════════════════════════════════════════
    public class FoolishIdleState : FoolishBossState
    {
        public override FoolishBossStateType StateType => FoolishBossStateType.Idle;

        private float _timer;

        public FoolishIdleState(FoolishBossController b, FoolishBossStateMachine m) : base(b, m) { }

        public override void Enter()
        {
            _timer = Boss.IdleDuration;
            Trigger("Idle");
            Boss.Feedback.Play(Profile.idle, Profile.idle.GetDuration(Boss.IdleDuration));
        }

        public override void Update(float dt)
        {
            if (Boss.IsDead) { ForceGo(FoolishBossStateType.Dead); return; }

            // 待機計時在躲避階段也照算：不然打鬥窗口一開始還要再發呆一整個待機時間才動
            _timer -= dt;

            // 躲避階段 Boss 只待機，不攻擊
            if (Boss.CurrentPhase == FoolishPhase.Evade) return;
            if (_timer > 0f) return;

            float dist = Boss.DistanceToPlayer;
            if (dist <= Boss.Attack1Range) Go(FoolishBossStateType.Attack1);
            else if (dist <= Boss.Attack2Range) Go(FoolishBossStateType.Attack2);
            else if (dist <= Boss.Attack3Range) Go(FoolishBossStateType.Attack3);
            else Go(FoolishBossStateType.Move);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  MoveState  移動接近玩家
    // ════════════════════════════════════════════════════════════════
    public class FoolishMoveState : FoolishBossState
    {
        public override FoolishBossStateType StateType => FoolishBossStateType.Move;

        public FoolishMoveState(FoolishBossController b, FoolishBossStateMachine m) : base(b, m) { }

        public override void Enter()
        {
            Trigger("Move");
            Boss.Feedback.Play(Profile.move, Profile.move.GetDuration(1f));
        }

        public override void FixedUpdate(float dt)
        {
            if (Boss.IsDead) { ForceGo(FoolishBossStateType.Dead); return; }

            // 躲避階段不移動
            if (Boss.CurrentPhase == FoolishPhase.Evade)
            {
                Go(FoolishBossStateType.Idle);
                return;
            }

            Boss.MoveTowardPlayer(dt);

            if (Boss.DistanceToPlayer <= Boss.Attack1Range)
                Go(FoolishBossStateType.Idle);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  AttackState  三種攻擊共用
    // ════════════════════════════════════════════════════════════════
    public class FoolishAttackState : FoolishBossState
    {
        private readonly FoolishBossStateType _type;
        private float _timer;

        public override FoolishBossStateType StateType => _type;

        public FoolishAttackState(FoolishBossController b, FoolishBossStateMachine m,
                                  FoolishBossStateType type) : base(b, m)
        {
            _type = type;
        }

        public override void Enter()
        {
            var entry = Profile.GetAttack(_type);
            // 動作時間跟著動畫長度走；沒有動畫時用保險時間，避免卡在攻擊狀態
            float duration = entry.GetDuration(Profile.attackFallbackDuration);
            _timer = duration;

            Boss.FacePlayer();
            Trigger(_type.ToString()); // Attack1 / Attack2 / Attack3
            Boss.Feedback.Play(entry, duration, onHit: () => Boss.PerformAttackHit(_type));
        }

        public override void Update(float dt)
        {
            if (Boss.IsDead) { ForceGo(FoolishBossStateType.Dead); return; }

            _timer -= dt;
            if (_timer <= 0f) Go(FoolishBossStateType.Idle);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  StaggerState  受擊硬直（選配）
    // ════════════════════════════════════════════════════════════════
    public class FoolishStaggerState : FoolishBossState
    {
        public override FoolishBossStateType StateType => FoolishBossStateType.Stagger;

        private float _timer;

        public FoolishStaggerState(FoolishBossController b, FoolishBossStateMachine m) : base(b, m) { }

        public override void Enter()
        {
            // 硬直時間：有設定就用設定值，0 = 依受擊動畫長度
            _timer = Profile.staggerDuration > 0f ? Profile.staggerDuration : Profile.hurt.GetDuration(0.5f);
            Boss.Feedback.CancelPending();   // 被打斷的招式還沒播的音效 / 特效 / 命中不要再觸發（受擊音效在 TakeDamage 已經立刻播了）
            Trigger("Stagger");
        }

        public override void Update(float dt)
        {
            if (Boss.IsDead) { ForceGo(FoolishBossStateType.Dead); return; }
            _timer -= dt;
            if (_timer <= 0f) Go(FoolishBossStateType.Idle);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  DeadState
    // ════════════════════════════════════════════════════════════════
    public class FoolishDeadState : FoolishBossState
    {
        public override FoolishBossStateType StateType => FoolishBossStateType.Dead;

        public FoolishDeadState(FoolishBossController b, FoolishBossStateMachine m) : base(b, m) { }

        public override void Enter()
        {
            Trigger("Dead");
            Boss.Feedback.Play(Profile.dead, Profile.dead.GetDuration(2f));
            Boss.OnDeath();
        }
    }
}