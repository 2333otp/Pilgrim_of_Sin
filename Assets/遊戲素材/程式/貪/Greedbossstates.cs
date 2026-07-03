using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    // ════════════════════════════════════════════════════════════════
    //  IdleState
    // ════════════════════════════════════════════════════════════════
    public class GreedIdleState : GreedBossState
    {
        public override GreedBossStateType StateType => GreedBossStateType.Idle;

        private float _timer;

        public GreedIdleState(GreedBossController b, GreedBossStateMachine m) : base(b, m) { }

        public override void Enter()
        {
            _timer = Boss.IdleDuration;
            Trigger("Idle");
        }

        public override void Update(float dt)
        {
            if (Boss.IsDead) { ForceGo(GreedBossStateType.Dead); return; }
            if (Boss.IsInBalanceWindow) return;

            _timer -= dt;
            if (_timer > 0f) return;

            float dist = Boss.DistanceToPlayer;
            if (dist <= Boss.Attack1Range)           Go(GreedBossStateType.Attack1);
            else if (dist <= Boss.Attack2Range)      Go(GreedBossStateType.Attack2);
            else if (dist <= Boss.Attack3Range)      Go(GreedBossStateType.Attack3);
            else                                     Go(GreedBossStateType.Move);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  MoveState
    // ════════════════════════════════════════════════════════════════
    public class GreedMoveState : GreedBossState
    {
        public override GreedBossStateType StateType => GreedBossStateType.Move;

        public GreedMoveState(GreedBossController b, GreedBossStateMachine m) : base(b, m) { }

        public override void Enter()
        {
            Trigger("Move");
        }

        public override void FixedUpdate(float dt)
        {
            if (Boss.IsDead) { ForceGo(GreedBossStateType.Dead); return; }
            if (Boss.IsInBalanceWindow) { Go(GreedBossStateType.Idle); return; }

            Boss.MoveTowardPlayer(dt);

            if (Boss.DistanceToPlayer <= Boss.Attack1Range)
                Go(GreedBossStateType.Idle);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  AttackState  （Attack1 / Attack2 / Attack3 共用）
    // ════════════════════════════════════════════════════════════════
    public class GreedAttackState : GreedBossState
    {
        private readonly GreedBossStateType _type;
        private bool _animDone;
        private float _fallbackTimer;
        private const float FallbackDuration = 3f;

        public override GreedBossStateType StateType => _type;

        public GreedAttackState(GreedBossController b, GreedBossStateMachine m,
                                GreedBossStateType type) : base(b, m)
        {
            _type = type;
        }

        public override void Enter()
        {
            _animDone = false;
            _fallbackTimer = 0f;
            string triggerName = _type switch
            {
                GreedBossStateType.Attack1 => "Attack1",
                GreedBossStateType.Attack2 => "Attack2",
                _ => "Attack3",
            };
            Trigger(triggerName);
            Boss.OnAttackAnimEnd += HandleAnimEnd;
        }

        public override void Update(float dt)
        {
            if (Boss.IsDead) { ForceGo(GreedBossStateType.Dead); return; }
            _fallbackTimer += dt;
            if (_animDone || _fallbackTimer >= FallbackDuration) Go(GreedBossStateType.Idle);
        }

        public override void Exit() => Boss.OnAttackAnimEnd -= HandleAnimEnd;

        private void HandleAnimEnd() => _animDone = true;
    }

    // ════════════════════════════════════════════════════════════════
    //  KickScaleState
    //  天秤平衡 10 秒窗口結束後，由 GreedBossController 強制觸發
    // ════════════════════════════════════════════════════════════════
    public class GreedKickScaleState : GreedBossState
    {
        public override GreedBossStateType StateType => GreedBossStateType.KickScale;

        private bool _animDone;
        private float _fallbackTimer;
        private const float FallbackDuration = 3f; // 動畫事件未設定時的備用等待秒數

        public GreedKickScaleState(GreedBossController b, GreedBossStateMachine m) : base(b, m) { }

        public override void Enter()
        {
            _animDone = false;
            _fallbackTimer = 0f;
            Trigger("KickScale");
            Boss.OnKickScaleAnimEnd += HandleAnimEnd;
        }

        public override void Update(float dt)
        {
            _fallbackTimer += dt;

            bool timedOut = _fallbackTimer >= FallbackDuration;
            if (timedOut && !_animDone)
                Debug.LogWarning($"[KickScale] ⚠️ {FallbackDuration}秒內未收到 AnimEvent_KickScaleEnd，強制結束（請確認動畫事件是否掛好）。");

            if (_animDone || timedOut)
            {
                Boss.ResetScale();
                Go(GreedBossStateType.Idle);
            }
        }

        public override void Exit() => Boss.OnKickScaleAnimEnd -= HandleAnimEnd;

        private void HandleAnimEnd() => _animDone = true;
    }

    // ════════════════════════════════════════════════════════════════
    //  StaggerState
    //  選配：需開啟 GreedBossController.enableStagger
    // ════════════════════════════════════════════════════════════════
    public class GreedStaggerState : GreedBossState
    {
        public override GreedBossStateType StateType => GreedBossStateType.Stagger;

        private float _timer;

        public GreedStaggerState(GreedBossController b, GreedBossStateMachine m) : base(b, m) { }

        public override void Enter()
        {
            _timer = Boss.StaggerDuration;
            Trigger("Stagger");
        }

        public override void Update(float dt)
        {
            if (Boss.IsDead) { ForceGo(GreedBossStateType.Dead); return; }
            _timer -= dt;
            if (_timer <= 0f) Go(GreedBossStateType.Idle);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  DeadState
    // ════════════════════════════════════════════════════════════════
    public class GreedDeadState : GreedBossState
    {
        public override GreedBossStateType StateType => GreedBossStateType.Dead;

        public GreedDeadState(GreedBossController b, GreedBossStateMachine m) : base(b, m) { }

        public override void Enter()
        {
            Trigger("Dead");
            Boss.OnDeath();
        }
    }
}