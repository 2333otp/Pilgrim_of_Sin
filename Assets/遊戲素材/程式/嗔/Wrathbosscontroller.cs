using System;
using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// Boss「嗔」主控制器。
    /// 負責：
    ///   - 組裝並驅動 WrathBossStateMachine
    ///   - 管理五芒星軌跡（5個定點，依序衝刺）
    ///   - 追蹤各定點的畫是否已被玩家改寫
    ///   - 持有所有 Inspector 可調數值
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class WrathBossController : MonoBehaviour, IDamageable
    {
        // ── References ───────────────────────────────────────────────
        [Header("References")]
        [SerializeField] private Transform _player;
        [SerializeField] private Collider _dashHitbox;       // 衝撞碰撞體
        [SerializeField] private Collider _explosionHitbox;  // 爆炸碰撞體

        /// <summary>五芒星的5個定點，依順序設定（Inspector拖入）。</summary>
        [SerializeField] private Transform[] _pentagramPoints = new Transform[5];

        /// <summary>各定點對應的畫物件。</summary>
        [SerializeField] private PaintingObject[] _paintings = new PaintingObject[5];

        public Animator Animator { get; private set; }

        // ── Boss 基本數值 ─────────────────────────────────────────────
        [Header("Boss Stats")]
        [SerializeField] private float _maxHp = 13000f;
        [SerializeField] private float _idleDuration = 0.8f;

        [Header("Attack Damage")]
        [SerializeField] private float _attack1Damage = 800f;
        [SerializeField] private float _attack2Damage = 1300f;
        [SerializeField] private float _attack3Damage = 1000f;
        [SerializeField] private float _dashDamage = 400f;   // 衝撞傷害
        [SerializeField] private float _explosionDamage = 600f;  // 爆炸傷害

        [Header("Attack Range")]
        [SerializeField] private float _attack1Range = 3f;
        [SerializeField] private float _attack2Range = 6f;
        [SerializeField] private float _attack3Range = 10f;

        // ── 五芒星衝刺數值 ────────────────────────────────────────────
        [Header("Dash")]
        [SerializeField] private float _dashSpeed = 18f;
        [SerializeField] private float _dashArriveThreshold = 0.3f; // 到達判定距離

        // ── 畫的等待窗口 ──────────────────────────────────────────────
        [Header("Painting Wait")]
        [SerializeField] public float WaitAtPaintingDuration = 10f; // Inspector 可調

        // ── Stagger（選配） ───────────────────────────────────────────
        [Header("Stagger (Optional)")]
        [SerializeField] public bool enableStagger = false;
        [SerializeField] public float StaggerDuration = 0.5f;

        // ── 公開屬性（各狀態讀取） ────────────────────────────────────
        public float CurrentHp { get; private set; }
        public bool IsDead => CurrentHp <= 0f;
        public float IdleDuration => _idleDuration;

        /// <summary>是否有下一個可衝刺的五芒星節點。</summary>
        public bool HasNextDashTarget => _pentagramPoints != null && _pentagramPoints.Length > 0;

        /// <summary>是否已到達目標點。</summary>
        public bool ReachedDashTarget
        {
            get
            {
                if (_currentTarget == null) return false;
                return Vector3.Distance(transform.position, _currentTarget.position)
                       <= _dashArriveThreshold;
            }
        }

        /// <summary>當前定點的畫是否已被玩家改寫。</summary>
        public bool CurrentPointPaintingModified
            => _currentPointIndex >= 0
            && _currentPointIndex < _paintings.Length
            && _paintings[_currentPointIndex] != null
            && _paintings[_currentPointIndex].IsModified;

        // ── 動畫事件 ─────────────────────────────────────────────────
        public event Action OnAttackAnimEnd;
        public event Action OnExplodeAnimEnd;

        // ── 內部 ─────────────────────────────────────────────────────
        private WrathBossStateMachine _fsm;
        private Transform _currentTarget;
        private int _currentPointIndex = -1;

        // 五芒星走法順序（0→2→4→1→3→0 依五芒星連線）
        private static readonly int[] PentagramOrder = { 0, 2, 4, 1, 3 };
        private int _pentagramStep = 0;

        // 至少改寫一幅畫才能受傷
        private bool _canTakeDamage = false;

        // ════════════════════════════════════════════════════════════
        //  Unity 生命周期
        // ════════════════════════════════════════════════════════════

        private void Awake()
        {
            Animator = GetComponent<Animator>();
            CurrentHp = _maxHp;

            if (_dashHitbox) _dashHitbox.enabled = false;
            if (_explosionHitbox) _explosionHitbox.enabled = false;

            BuildFSM();
        }

        private void Start()
        {
            // 訂閱所有畫的改寫事件
            foreach (var p in _paintings)
                if (p != null) p.OnModified += HandlePaintingModified;
        }

        private void Update() => _fsm.Update(Time.deltaTime);
        private void FixedUpdate() => _fsm.FixedUpdate(Time.fixedDeltaTime);

        private void OnDestroy()
        {
            foreach (var p in _paintings)
                if (p != null) p.OnModified -= HandlePaintingModified;
        }

        // ════════════════════════════════════════════════════════════
        //  FSM 組裝
        // ════════════════════════════════════════════════════════════

        private void BuildFSM()
        {
            _fsm = new WrathBossStateMachine();

            var states = new Dictionary<WrathBossStateType, WrathBossState>
            {
                { WrathBossStateType.Idle,           new WrathIdleState(this, _fsm)                              },
                { WrathBossStateType.Dash,           new WrathDashState(this, _fsm)                              },
                { WrathBossStateType.Explode,        new WrathExplodeState(this, _fsm)                           },
                { WrathBossStateType.WaitAtPainting, new WrathWaitAtPaintingState(this, _fsm)                    },
                { WrathBossStateType.Attack1,        new WrathAttackState(this, _fsm, WrathBossStateType.Attack1)},
                { WrathBossStateType.Attack2,        new WrathAttackState(this, _fsm, WrathBossStateType.Attack2)},
                { WrathBossStateType.Attack3,        new WrathAttackState(this, _fsm, WrathBossStateType.Attack3)},
                { WrathBossStateType.Stagger,        new WrathStaggerState(this, _fsm)                           },
                { WrathBossStateType.Dead,           new WrathDeadState(this, _fsm)                              },
            };

            _fsm.Init(states, WrathBossStateType.Idle);
        }

        // ════════════════════════════════════════════════════════════
        //  五芒星衝刺
        // ════════════════════════════════════════════════════════════

        /// <summary>DashState.Enter 呼叫，設定下一個目標點。</summary>
        public void StartDashToNextTarget()
        {
            _pentagramStep = (_pentagramStep + 1) % PentagramOrder.Length;
            _currentPointIndex = PentagramOrder[_pentagramStep];

            if (_currentPointIndex < _pentagramPoints.Length)
                _currentTarget = _pentagramPoints[_currentPointIndex];

            Debug.Log($"[Wrath] 衝向第 {_currentPointIndex} 點");
        }

        /// <summary>DashState.FixedUpdate 呼叫，推進位置。</summary>
        public void MoveDash(float dt)
        {
            if (_currentTarget == null) return;
            Vector3 dir = (_currentTarget.position - transform.position).normalized;
            dir.y = 0f;
            transform.position += dir * _dashSpeed * dt;
            if (dir != Vector3.zero)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                                                       Quaternion.LookRotation(dir), 20f * dt);
        }

        public void EnableDashHitbox(bool v)
        {
            if (_dashHitbox) _dashHitbox.enabled = v;
        }

        // ════════════════════════════════════════════════════════════
        //  攻擊決策
        // ════════════════════════════════════════════════════════════

        public WrathBossStateType DecideAttack()
        {
            float dist = _player != null
                         ? Vector3.Distance(transform.position, _player.position)
                         : float.MaxValue;

            if (dist <= _attack1Range) return WrathBossStateType.Attack1;
            if (dist <= _attack2Range) return WrathBossStateType.Attack2;
            if (dist <= _attack3Range) return WrathBossStateType.Attack3;
            return WrathBossStateType.Idle; // 太遠就等待
        }

        public float GetAttackDamage(WrathBossStateType type)
        {
            return type switch
            {
                WrathBossStateType.Attack1 => _attack1Damage,
                WrathBossStateType.Attack2 => _attack2Damage,
                _ => _attack3Damage,
            };
        }

        // ════════════════════════════════════════════════════════════
        //  畫的改寫事件
        // ════════════════════════════════════════════════════════════

        private void HandlePaintingModified()
        {
            // 至少一幅畫被改寫後，Boss 才可受傷
            _canTakeDamage = true;
            Debug.Log("[Wrath] 畫已被改寫，Boss 現在可受傷。");
        }

        // ════════════════════════════════════════════════════════════
        //  戰鬥介面
        // ════════════════════════════════════════════════════════════

        public void TakeDamage(float amount)
        {
            if (!_canTakeDamage)
            {
                Debug.Log("[Wrath] 尚未改寫任何畫，傷害無效。");
                return;
            }

            CurrentHp = Mathf.Max(0f, CurrentHp - amount);
            Debug.Log($"[Wrath] 受傷 {amount}，剩餘 HP {CurrentHp}");

            if (CurrentHp <= 0f)
            {
                _fsm.Force(WrathBossStateType.Dead);
                return;
            }

            if (enableStagger)
                _fsm.Request(WrathBossStateType.Stagger);
        }

        public void OnDeath()
        {
            // TODO: 觸發通關演出
            Debug.Log("[Wrath] Boss 死亡，通關。");
        }

        // ════════════════════════════════════════════════════════════
        //  Animation Events
        // ════════════════════════════════════════════════════════════

        public void AnimEvent_AttackEnd() => OnAttackAnimEnd?.Invoke();
        public void AnimEvent_ExplodeEnd() => OnExplodeAnimEnd?.Invoke();

        /// <summary>爆炸動畫特定幀：啟用爆炸碰撞體。</summary>
        public void AnimEvent_EnableExplosion()
        {
            if (_explosionHitbox) _explosionHitbox.enabled = true;
        }

        /// <summary>爆炸動畫結束後：關閉爆炸碰撞體。</summary>
        public void AnimEvent_DisableExplosion()
        {
            if (_explosionHitbox) _explosionHitbox.enabled = false;
        }
    }
}