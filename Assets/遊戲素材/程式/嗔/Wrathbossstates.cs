using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    // ════════════════════════════════════════════════════════════════
    //  WrathBossState  抽象基底
    // ════════════════════════════════════════════════════════════════
    public abstract class WrathBossState
    {
        protected WrathBossController Boss { get; }
        protected WrathBossStateMachine Machine { get; }
        protected Animator Anim => Boss.Animator;

        public abstract WrathBossStateType StateType { get; }

        protected WrathBossState(WrathBossController boss, WrathBossStateMachine machine)
        {
            Boss = boss;
            Machine = machine;
        }

        public virtual void Enter() { }
        public virtual void Update(float dt) { }
        public virtual void FixedUpdate(float dt) { }
        public virtual void Exit() { }

        protected void Go(WrathBossStateType next) => Machine.Request(next);
        protected void ForceGo(WrathBossStateType next) => Machine.Force(next);
        protected void Trigger(string name) => Anim.SetTrigger(name);
        protected void SetBool(string name, bool v) => Anim.SetBool(name, v);
    }

    // ════════════════════════════════════════════════════════════════
    //  IdleState  待機決策
    // ════════════════════════════════════════════════════════════════
    public class WrathIdleState : WrathBossState
    {
        public override WrathBossStateType StateType => WrathBossStateType.Idle;

        private float _timer;

        public WrathIdleState(WrathBossController b, WrathBossStateMachine m) : base(b, m) { }

        public override void Enter()
        {
            _timer = Boss.IdleDuration;
            Trigger("Idle");
        }

        public override void Update(float dt)
        {
            if (Boss.IsDead) { ForceGo(WrathBossStateType.Dead); return; }

            _timer -= dt;
            if (_timer > 0f) return;

            // 有下一個五芒星節點就衝刺，否則普通攻擊
            if (Boss.HasNextDashTarget)
                Go(WrathBossStateType.Dash);
            else
            {
                var atk = Boss.DecideAttack();
                Go(atk);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  DashState  沿五芒星軌跡衝刺
    //  衝撞判定（400）由 DashHitbox 碰撞體負責
    // ════════════════════════════════════════════════════════════════
    public class WrathDashState : WrathBossState
    {
        public override WrathBossStateType StateType => WrathBossStateType.Dash;

        public WrathDashState(WrathBossController b, WrathBossStateMachine m) : base(b, m) { }

        public override void Enter()
        {
            Trigger("Dash");
            Boss.EnableDashHitbox(true);   // 開啟衝撞碰撞體
            Boss.StartDashToNextTarget();
        }

        public override void FixedUpdate(float dt)
        {
            if (Boss.IsDead) { ForceGo(WrathBossStateType.Dead); return; }

            Boss.MoveDash(dt);

            // 抵達目標點 → 爆炸
            if (Boss.ReachedDashTarget)
                Go(WrathBossStateType.Explode);
        }

        public override void Exit() => Boss.EnableDashHitbox(false);
    }

    // ════════════════════════════════════════════════════════════════
    //  ExplodeState  抵達定點後爆炸
    //  爆炸判定（600）由 ExplosionHitbox 負責，Animation Event 觸發
    // ════════════════════════════════════════════════════════════════
    public class WrathExplodeState : WrathBossState
    {
        public override WrathBossStateType StateType => WrathBossStateType.Explode;

        private bool _animDone;

        public WrathExplodeState(WrathBossController b, WrathBossStateMachine m) : base(b, m) { }

        public override void Enter()
        {
            _animDone = false;
            Trigger("Explode");
            Boss.OnExplodeAnimEnd += HandleAnimEnd;
        }

        public override void Update(float dt)
        {
            if (Boss.IsDead) { ForceGo(WrathBossStateType.Dead); return; }
            if (!_animDone) return;

            // 判斷這個定點的畫是否已被改寫
            if (Boss.CurrentPointPaintingModified)
                Go(WrathBossStateType.WaitAtPainting);
            else
                Go(WrathBossStateType.Idle); // 繼續下一段衝刺
        }

        public override void Exit() => Boss.OnExplodeAnimEnd -= HandleAnimEnd;

        private void HandleAnimEnd() => _animDone = true;
    }

    // ════════════════════════════════════════════════════════════════
    //  WaitAtPaintingState  停在改寫過的畫前（玩家攻擊窗口）
    // ════════════════════════════════════════════════════════════════
    public class WrathWaitAtPaintingState : WrathBossState
    {
        public override WrathBossStateType StateType => WrathBossStateType.WaitAtPainting;

        private float _timer;

        public WrathWaitAtPaintingState(WrathBossController b, WrathBossStateMachine m) : base(b, m) { }

        public override void Enter()
        {
            _timer = Boss.WaitAtPaintingDuration;
            Trigger("Wait");
            Debug.Log($"[Wrath] 停在改寫的畫前，{_timer}s 攻擊窗口。");
        }

        public override void Update(float dt)
        {
            if (Boss.IsDead) { ForceGo(WrathBossStateType.Dead); return; }

            _timer -= dt;
            if (_timer <= 0f)
                Go(WrathBossStateType.Idle);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  AttackState  三種攻擊共用
    // ════════════════════════════════════════════════════════════════
    public class WrathAttackState : WrathBossState
    {
        private readonly WrathBossStateType _type;
        private bool _animDone;

        public override WrathBossStateType StateType => _type;

        public WrathAttackState(WrathBossController b, WrathBossStateMachine m,
                                WrathBossStateType type) : base(b, m)
        {
            _type = type;
        }

        public override void Enter()
        {
            _animDone = false;
            string triggerName = _type switch
            {
                WrathBossStateType.Attack1 => "Attack1",
                WrathBossStateType.Attack2 => "Attack2",
                _ => "Attack3",
            };
            Trigger(triggerName);
            Boss.OnAttackAnimEnd += HandleAnimEnd;
        }

        public override void Update(float dt)
        {
            if (Boss.IsDead) { ForceGo(WrathBossStateType.Dead); return; }
            if (_animDone) Go(WrathBossStateType.Idle);
        }

        public override void Exit() => Boss.OnAttackAnimEnd -= HandleAnimEnd;

        private void HandleAnimEnd() => _animDone = true;
    }

    // ════════════════════════════════════════════════════════════════
    //  StaggerState  受擊硬直（選配）
    // ════════════════════════════════════════════════════════════════
    public class WrathStaggerState : WrathBossState
    {
        public override WrathBossStateType StateType => WrathBossStateType.Stagger;

        private float _timer;

        public WrathStaggerState(WrathBossController b, WrathBossStateMachine m) : base(b, m) { }

        public override void Enter()
        {
            _timer = Boss.StaggerDuration;
            Trigger("Stagger");
        }

        public override void Update(float dt)
        {
            if (Boss.IsDead) { ForceGo(WrathBossStateType.Dead); return; }
            _timer -= dt;
            if (_timer <= 0f) Go(WrathBossStateType.Idle);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  DeadState
    // ════════════════════════════════════════════════════════════════
    public class WrathDeadState : WrathBossState
    {
        public override WrathBossStateType StateType => WrathBossStateType.Dead;

        public WrathDeadState(WrathBossController b, WrathBossStateMachine m) : base(b, m) { }

        public override void Enter()
        {
            Trigger("Dead");
            Boss.OnDeath();
        }
    }
}