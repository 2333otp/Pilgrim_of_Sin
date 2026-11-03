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
        // 癡還沒綁 Animator Controller（暫時是膠囊）時直接略過，避免每次切換狀態都洗 "Animator is not playing an AnimatorController" 警告
        protected void Trigger(string name)
        {
            if (Anim.runtimeAnimatorController != null) Anim.SetTrigger(name);
        }

        protected void SetBool(string name, bool v)
        {
            if (Anim.runtimeAnimatorController != null) Anim.SetBool(name, v);
        }
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
        }

        public override void Update(float dt)
        {
            if (Boss.IsDead) { ForceGo(FoolishBossStateType.Dead); return; }

            // 躲避階段 Boss 只待機，不攻擊
            if (Boss.CurrentPhase == FoolishPhase.Evade) return;

            _timer -= dt;
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

        public override void Enter() => Trigger("Move");

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
        private bool _animDone;
        private float _fallbackTimer;

        public override FoolishBossStateType StateType => _type;

        public FoolishAttackState(FoolishBossController b, FoolishBossStateMachine m,
                                  FoolishBossStateType type) : base(b, m)
        {
            _type = type;
        }

        public override void Enter()
        {
            _animDone = false;
            _fallbackTimer = Boss.AttackFallbackDuration;
            string triggerName = _type switch
            {
                FoolishBossStateType.Attack1 => "Attack1",
                FoolishBossStateType.Attack2 => "Attack2",
                _ => "Attack3",
            };
            Trigger(triggerName);
            Boss.Voice?.PlayAttack(_type);
            Boss.OnAttackAnimEnd += HandleAnimEnd;
        }

        public override void Update(float dt)
        {
            if (Boss.IsDead) { ForceGo(FoolishBossStateType.Dead); return; }

            // 還沒綁 Animator Controller 時不會有動畫事件，靠保險計時結束，避免卡在攻擊狀態
            if (Anim.runtimeAnimatorController == null)
            {
                _fallbackTimer -= dt;
                if (_fallbackTimer <= 0f) _animDone = true;
            }

            if (_animDone) Go(FoolishBossStateType.Idle);
        }

        public override void Exit() => Boss.OnAttackAnimEnd -= HandleAnimEnd;

        private void HandleAnimEnd() => _animDone = true;
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
            _timer = Boss.StaggerDuration;
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
            Boss.Voice?.Play(FoolishVoiceId.Dead);
            Boss.OnDeath();
        }
    }
}