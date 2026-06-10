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

            // 永遠有五芒星頂點可衝，所以 Idle 後必定進 Dash
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
    //  - 衝撞判定（400）由 DashHitbox 碰撞體負責
    //  - 每移動 _pathSpawnInterval 距離生成一個 PathDamageTrigger（殘留 5 秒，400 傷害）
    // ════════════════════════════════════════════════════════════════
    public class WrathDashState : WrathBossState
    {
        public override WrathBossStateType StateType => WrathBossStateType.Dash;

        public WrathDashState(WrathBossController b, WrathBossStateMachine m) : base(b, m) { }

        public override void Enter()
        {
            Trigger("Dash");
            Boss.EnableDashHitbox(true);
            Boss.StartDashToNextTarget();
        }

        public override void FixedUpdate(float dt)
        {
            if (Boss.IsDead) { ForceGo(WrathBossStateType.Dead); return; }

            Boss.MoveDash(dt);

            if (Boss.ReachedDashTarget)
                Go(WrathBossStateType.StayAtVertex);
        }

        public override void Exit() => Boss.EnableDashHitbox(false);
    }

    // ════════════════════════════════════════════════════════════════
    //  StayAtVertexState  抵達頂點後停留 10 秒
    //
    //  進入時：
    //    - 畫未改 → 觸發爆炸動畫（Animation Event 開關 ExplosionHitbox，傷害 600）
    //    - 畫已改 → 播待機動畫，不爆炸
    //    - 同時開始 10 秒計時（爆炸時間算在 10 秒內）
    //
    //  出招排程（placeholder）：
    //    - 第一次：3~4 秒（固定觸發）
    //    - 第二次：7~8 秒（50% 機率觸發）
    //    - 出招使用 Boss.DecideAttack() 依距離選招式
    //    - 出招動畫在 StayAtVertex 內部處理，不離開此狀態
    //
    //  10 秒到且不在出招中 → 回 Idle → 下一輪 Dash
    // ════════════════════════════════════════════════════════════════
    public class WrathStayAtVertexState : WrathBossState
    {
        public override WrathBossStateType StateType => WrathBossStateType.StayAtVertex;

        private const float AttackFallbackDuration = 1.5f;

        private float _elapsed;
        private float _firstAttackAt;
        private float _secondAttackAt;
        private bool _doSecondAttack;
        private bool _firstAttackFired;
        private bool _secondAttackFired;
        private bool _isAttacking;
        private float _attackTimer;

        public WrathStayAtVertexState(WrathBossController b, WrathBossStateMachine m) : base(b, m) { }

        public override void Enter()
        {
            _elapsed = 0f;
            _firstAttackAt = Random.Range(3f, 4f);
            _secondAttackAt = Random.Range(7f, 8f);
            _doSecondAttack = Random.value > 0.5f;
            _firstAttackFired = false;
            _secondAttackFired = false;
            _isAttacking = false;

            if (!Boss.CurrentPointPaintingModified)
                Trigger("Explode"); // Animation Event 負責開關 ExplosionHitbox
            else
                Trigger("Wait");
        }

        public override void Update(float dt)
        {
            if (Boss.IsDead) { ForceGo(WrathBossStateType.Dead); return; }

            _elapsed += dt;

            if (_isAttacking)
            {
                _attackTimer += dt;
                if (_attackTimer >= AttackFallbackDuration)
                    OnAttackComplete();
                return;
            }

            if (!_firstAttackFired && _elapsed >= _firstAttackAt)
            {
                _firstAttackFired = true;
                FireAttack();
                return;
            }

            if (_doSecondAttack && !_secondAttackFired && _elapsed >= _secondAttackAt)
            {
                _secondAttackFired = true;
                FireAttack();
                return;
            }

            if (_elapsed >= Boss.StayAtVertexDuration)
                Go(WrathBossStateType.Idle);
        }

        private void FireAttack()
        {
            _isAttacking = true;
            _attackTimer = 0f;

            var attackType = Boss.DecideAttack();
            if (attackType == WrathBossStateType.Idle)
                attackType = WrathBossStateType.Attack1;

            string triggerName = attackType switch
            {
                WrathBossStateType.Attack1 => "Attack1",
                WrathBossStateType.Attack2 => "Attack2",
                _ => "Attack3",
            };

            Trigger(triggerName);
            Boss.OnAttackAnimEnd += OnAttackComplete;
        }

        private void OnAttackComplete()
        {
            if (!_isAttacking) return;
            Boss.OnAttackAnimEnd -= OnAttackComplete;
            _isAttacking = false;
            _attackTimer = 0f;
            Trigger("Wait");
        }

        public override void Exit()
        {
            // 安全清理：若在出招中途被強制切走，確保事件不殘留
            Boss.OnAttackAnimEnd -= OnAttackComplete;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  AttackState  三種攻擊共用（目前主要由 IdleState 的 DecideAttack 路徑觸發）
    // ════════════════════════════════════════════════════════════════
    public class WrathAttackState : WrathBossState
    {
        private const float AttackFallbackDuration = 1.5f;

        private readonly WrathBossStateType _type;
        private bool _animDone;
        private float _timer;

        public override WrathBossStateType StateType => _type;

        public WrathAttackState(WrathBossController b, WrathBossStateMachine m,
                                WrathBossStateType type) : base(b, m)
        {
            _type = type;
        }

        public override void Enter()
        {
            _animDone = false;
            _timer = 0f;
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
            _timer += dt;
            if (_animDone || _timer >= AttackFallbackDuration)
                Go(WrathBossStateType.Idle);
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
