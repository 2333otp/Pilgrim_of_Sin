using System;
using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// Boss「嗔」主控制器。
    /// 負責：
    ///   - 組裝並驅動 WrathBossStateMachine
    ///   - 管理五芒星軌跡（5 個定點，依序衝刺）
    ///   - 追蹤各定點的畫是否已被玩家改寫
    ///   - 持有所有 Inspector 可調數值
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class WrathBossController : MonoBehaviour, IDamageable
    {
        // ── References ───────────────────────────────────────────────
        [Header("References")]
        [SerializeField] private Transform _player;
        [SerializeField] private Collider _dashHitbox;
        [SerializeField] private Collider _explosionHitbox;

        /// <summary>五芒星的 5 個定點，依順序設定（Inspector 拖入）。</summary>
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
        [SerializeField] private float _dashDamage = 400f;
        [SerializeField] private float _explosionDamage = 600f;

        [Header("Attack Range")]
        [SerializeField] private float _attack1Range = 3f;
        [SerializeField] private float _attack2Range = 6f;
        [SerializeField] private float _attack3Range = 10f;

        // ── 五芒星衝刺數值 ────────────────────────────────────────────
        [Header("Dash")]
        [SerializeField] private float _dashSpeed = 18f;
        [SerializeField] private float _dashArriveThreshold = 0.3f;

        // ── 頂點停留窗口 ──────────────────────────────────────────────
        [Header("Stay At Vertex")]
        [SerializeField] public float StayAtVertexDuration = 10f; // Inspector 可調

        // ── 路徑殘留傷害 ──────────────────────────────────────────────
        [Header("Path Damage")]
        [SerializeField] private GameObject _pathDamagePrefab;
        [SerializeField] private float _pathSpawnInterval = 1f; // 每移動多少距離生成一個

        // ── 傷害減免（未改任何畫時） ──────────────────────────────────
        [Header("Damage Reduction (No Paintings Modified)")]
        [SerializeField] private float _noModifyDamageMultiplier = 0.1f; // 降低 90%，Inspector 可調

        // ── 玩家防禦增益（改第一幅畫後觸發） ─────────────────────────
        [Header("Player Defense Bonus")]
        [SerializeField] private float _playerDefenseBonusMultiplier = 0.8f; // 降低 20%，Inspector 可調

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

        // 五芒星走法：0→2→4→1→3→0（對應設計文件 1→3→5→2→4→1）
        private static readonly int[] PentagramOrder = { 0, 2, 4, 1, 3 };
        private int _pentagramStep = 4; // 初始為 Length-1，使第一次 +1 後從 index 0 開始

        private bool _canTakeDamage = false;
        private int _modifiedPaintingCount = 0;
        private float _pathSpawnDistAcc = 0f;

        public WrathBossStateType CurrentState => _fsm?.CurrentType ?? WrathBossStateType.Idle;

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

            // 移動由程式碼直接設定 position，不需要物理模擬
            var rb = GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            // 五芒星頂點解除父層，保持世界座標固定，不隨 Boss 移動
            foreach (var pt in _pentagramPoints)
                if (pt != null) pt.SetParent(null, true);
        }

        private void Start()
        {
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
                { WrathBossStateType.Idle,         new WrathIdleState(this, _fsm)                               },
                { WrathBossStateType.Dash,         new WrathDashState(this, _fsm)                               },
                { WrathBossStateType.StayAtVertex, new WrathStayAtVertexState(this, _fsm)                       },
                { WrathBossStateType.Attack1,      new WrathAttackState(this, _fsm, WrathBossStateType.Attack1)  },
                { WrathBossStateType.Attack2,      new WrathAttackState(this, _fsm, WrathBossStateType.Attack2)  },
                { WrathBossStateType.Attack3,      new WrathAttackState(this, _fsm, WrathBossStateType.Attack3)  },
                { WrathBossStateType.Stagger,      new WrathStaggerState(this, _fsm)                             },
                { WrathBossStateType.Dead,         new WrathDeadState(this, _fsm)                                },
            };

            _fsm.Init(states, WrathBossStateType.Idle);
        }

        // ════════════════════════════════════════════════════════════
        //  五芒星衝刺
        // ════════════════════════════════════════════════════════════

        /// <summary>DashState.Enter 呼叫，設定下一個目標點並重置路徑計數器。</summary>
        public void StartDashToNextTarget()
        {
            _pentagramStep = (_pentagramStep + 1) % PentagramOrder.Length;
            _currentPointIndex = PentagramOrder[_pentagramStep];

            if (_currentPointIndex < _pentagramPoints.Length)
                _currentTarget = _pentagramPoints[_currentPointIndex];

            _pathSpawnDistAcc = 0f;
            Debug.Log($"[Wrath] 衝向第 {_currentPointIndex} 點");
        }

        /// <summary>DashState.FixedUpdate 呼叫，推進位置並沿路生成路徑殘留傷害。</summary>
        public void MoveDash(float dt)
        {
            if (_currentTarget == null) return;
            Vector3 dir = (_currentTarget.position - transform.position).normalized;
            dir.y = 0f;
            Vector3 movement = dir * _dashSpeed * dt;

            transform.position += movement;

            _pathSpawnDistAcc += movement.magnitude;
            if (_pathSpawnDistAcc >= _pathSpawnInterval && _pathDamagePrefab != null)
            {
                _pathSpawnDistAcc = 0f;
                Instantiate(_pathDamagePrefab, transform.position, Quaternion.identity);
            }

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
            return WrathBossStateType.Idle;
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
            _modifiedPaintingCount++;
            _canTakeDamage = true;

            // 每改一幅畫：Boss 立刻扣除 5% HP
            CurrentHp = Mathf.Max(0f, CurrentHp - _maxHp * 0.05f);
            Debug.Log($"[Wrath] 第 {_modifiedPaintingCount} 幅畫改寫，Boss 扣除 5% HP，剩餘 HP {CurrentHp}");

            // 改第一幅畫：觸發玩家防禦增益
            if (_modifiedPaintingCount == 1)
            {
                var player = _player?.GetComponent<PlayerController>();
                player?.ApplyDefenseBonus(_playerDefenseBonusMultiplier);
                Debug.Log("[Wrath] 玩家獲得防禦增益。");
            }

            if (CurrentHp <= 0f)
                _fsm.Force(WrathBossStateType.Dead);
        }

        // ════════════════════════════════════════════════════════════
        //  戰鬥介面
        // ════════════════════════════════════════════════════════════

        public void TakeDamage(float amount)
        {
            float actualAmount = _canTakeDamage ? amount : amount * _noModifyDamageMultiplier;
            CurrentHp = Mathf.Max(0f, CurrentHp - actualAmount);
            Debug.Log($"[Wrath] 受傷 {actualAmount}，剩餘 HP {CurrentHp}");

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
            Debug.Log("[Wrath] Boss 死亡，通關。");
            BossResultPortal.Instance?.OnBossDefeated();
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
