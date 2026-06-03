using System;
using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// Boss「貪」主控制器。
    /// 負責：
    ///   - 組裝並驅動 GreedBossStateMachine（AI 行為層）
    ///   - 管理天秤相位（ScalePhase）與 10 秒攻擊窗口計時
    ///   - 整合 MoneybagSpawner 控制錢袋生成/清除
    ///   - 持有所有 Inspector 可調數值
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class GreedBossController : MonoBehaviour, IDamageable
    {
        // ── References ───────────────────────────────────────────────
        [Header("References")]
        [SerializeField] private Transform _player;
        [SerializeField] private ScaleObject _scale;         // 天秤場景物件
        [SerializeField] private Collider _scaleHitbox;   // 天秤踢翻碰撞體
        [SerializeField] private MoneybagSpawner _spawner;       // 錢袋生成器

        public Animator Animator { get; private set; }

        // ── Boss 基本數值 ─────────────────────────────────────────────
        [Header("Boss Stats")]
        [SerializeField] private float _maxHp = 12000f;
        [SerializeField] private float _moveSpeed = 4f;
        [SerializeField] private float _idleDuration = 1f;

        [Header("Attack Range")]
        [SerializeField] private float _attack1Range = 3f;
        [SerializeField] private float _attack2Range = 6f;
        [SerializeField] private float _attack3Range = 10f;

        [Header("Attack Damage")]
        [SerializeField] private float _attack1Damage = 900f;
        [SerializeField] private float _attack2Damage = 1200f;
        [SerializeField] private float _attack3Damage = 2300f;

        // ── 天秤機制數值 ──────────────────────────────────────────────
        [Header("Scale Mechanic")]
        [SerializeField] private float _balanceWindowDuration = 10f;  // 攻擊窗口秒數
        [SerializeField] private float _attackBoostMultiplier = 1.5f; // 天秤傾斜時攻擊倍率
        [SerializeField] private float _scaleKickDamage = 700f; // 天秤踢翻傷害

        // ── Stagger（選配） ───────────────────────────────────────────
        [Header("Stagger (Optional)")]
        [SerializeField] public bool enableStagger = false;
        [SerializeField] public float StaggerDuration = 0.5f;

        // ── 公開屬性 ──────────────────────────────────────────────────
        public float CurrentHp { get; private set; }
        public bool IsDead => CurrentHp <= 0f;
        public float IdleDuration => _idleDuration;
        public float Attack1Range => _attack1Range;
        public float Attack2Range => _attack2Range;
        public float Attack3Range => _attack3Range;
        public float DistanceToPlayer => _player != null
            ? Vector3.Distance(transform.position, _player.position)
            : float.MaxValue;

        // ── 天秤相位 ──────────────────────────────────────────────────
        public ScalePhase CurrentPhase { get; private set; } = ScalePhase.Unbalanced;

        // ── 動畫事件 ──────────────────────────────────────────────────
        public event Action OnAttackAnimEnd;
        public event Action OnKickScaleAnimEnd;

        // ── 內部 ──────────────────────────────────────────────────────
        private GreedBossStateMachine _fsm;
        private float _balanceTimer;
        private float _currentAttackMultiplier = 1f;

        // ════════════════════════════════════════════════════════════
        //  Unity 生命週期
        // ════════════════════════════════════════════════════════════

        private void Awake()
        {
            Animator = GetComponent<Animator>();
            CurrentHp = _maxHp;
            if (_scaleHitbox) _scaleHitbox.enabled = false;
            BuildFSM();
        }

        private void Start()
        {
            if (_scale != null)
                _scale.OnWeightChanged += HandleScaleWeightChanged;

            // 第一次循環生成錢袋
            _spawner?.SpawnCycle();
        }

        private void Update()
        {
            // 暫停時跳過（坑 #9）
            if (Time.timeScale == 0f) return;

            UpdateBalanceWindow(Time.deltaTime);
            _fsm.Update(Time.deltaTime);
        }

        private void FixedUpdate()
        {
            if (Time.timeScale == 0f) return;
            _fsm.FixedUpdate(Time.fixedDeltaTime);
        }

        private void OnDestroy()
        {
            if (_scale != null)
                _scale.OnWeightChanged -= HandleScaleWeightChanged;
        }

        // ════════════════════════════════════════════════════════════
        //  FSM 組裝
        // ════════════════════════════════════════════════════════════

        private void BuildFSM()
        {
            _fsm = new GreedBossStateMachine();

            var states = new Dictionary<GreedBossStateType, GreedBossState>
            {
                { GreedBossStateType.Idle,      new GreedIdleState(this, _fsm)                               },
                { GreedBossStateType.Move,      new GreedMoveState(this, _fsm)                               },
                { GreedBossStateType.Attack1,   new GreedAttackState(this, _fsm, GreedBossStateType.Attack1) },
                { GreedBossStateType.Attack2,   new GreedAttackState(this, _fsm, GreedBossStateType.Attack2) },
                { GreedBossStateType.Attack3,   new GreedAttackState(this, _fsm, GreedBossStateType.Attack3) },
                { GreedBossStateType.KickScale, new GreedKickScaleState(this, _fsm)                         },
                { GreedBossStateType.Stagger,   new GreedStaggerState(this, _fsm)                           },
                { GreedBossStateType.Dead,      new GreedDeadState(this, _fsm)                              },
            };

            _fsm.Init(states, GreedBossStateType.Idle);
        }

        // ════════════════════════════════════════════════════════════
        //  天秤相位管理
        // ════════════════════════════════════════════════════════════

        /// <summary>天秤重量變化時由 ScaleObject 呼叫。</summary>
        private void HandleScaleWeightChanged(float rightWeight)
        {
            // 踢翻動畫播放中不切換相位
            if (CurrentPhase == ScalePhase.Kicked) return;

            if (_scale.IsBalanced())
            {
                if (CurrentPhase != ScalePhase.Balanced)
                    EnterBalancedPhase();
            }
            else
            {
                // 左重或右重都算 Unbalanced
                if (CurrentPhase == ScalePhase.Balanced)
                    EnterUnbalancedPhase();
            }
        }

        private void EnterBalancedPhase()
        {
            CurrentPhase = ScalePhase.Balanced;
            _balanceTimer = _balanceWindowDuration;
            _currentAttackMultiplier = 1f;
            Debug.Log("[Greed] 天秤平衡，10 秒攻擊窗口開始。");
        }

        private void EnterUnbalancedPhase()
        {
            CurrentPhase = ScalePhase.Unbalanced;
            _balanceTimer = 0f;
            _currentAttackMultiplier = _attackBoostMultiplier;
            Debug.Log("[Greed] 天秤傾斜，Boss 無敵＋攻擊力提升。");
        }

        private void UpdateBalanceWindow(float dt)
        {
            if (CurrentPhase != ScalePhase.Balanced) return;

            _balanceTimer -= dt;
            if (_balanceTimer <= 0f)
            {
                CurrentPhase = ScalePhase.Kicked;
                _fsm.Force(GreedBossStateType.KickScale);
                Debug.Log("[Greed] 10 秒到，強制 KickScale。");
            }
        }

        // ════════════════════════════════════════════════════════════
        //  循環重置（由 KickScaleState 動畫結束後呼叫）
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// KickScaleState 動畫結束後呼叫。
        /// 重置天秤重量，清除舊錢袋，生成新一批錢袋。
        /// </summary>
        public void ResetScale()
        {
            CurrentPhase = ScalePhase.Unbalanced;
            _currentAttackMultiplier = _attackBoostMultiplier;

            // 天秤視覺與重量歸零
            _scale?.ResetScale();

            // 清除舊錢袋並生成新一批（新循環開始）
            _spawner?.SpawnCycle();

            Debug.Log("[Greed] 天秤重置，新循環錢袋生成。");
        }

        // ════════════════════════════════════════════════════════════
        //  戰鬥介面
        // ════════════════════════════════════════════════════════════

        public void TakeDamage(float amount)
        {
            // 天秤傾斜時 Boss 無敵
            if (CurrentPhase == ScalePhase.Unbalanced)
            {
                Debug.Log("[Greed] 無敵中，傷害無效。");
                return;
            }

            CurrentHp = Mathf.Max(0f, CurrentHp - amount);
            Debug.Log($"[Greed] 受傷 {amount}，剩餘 HP {CurrentHp}");

            if (CurrentHp <= 0f)
            {
                _fsm.Force(GreedBossStateType.Dead);
                return;
            }

            if (enableStagger)
                _fsm.Request(GreedBossStateType.Stagger);
        }

        public float GetAttackDamage(GreedBossStateType attackType)
        {
            float baseDmg = attackType switch
            {
                GreedBossStateType.Attack1 => _attack1Damage,
                GreedBossStateType.Attack2 => _attack2Damage,
                _ => _attack3Damage,
            };
            return baseDmg * _currentAttackMultiplier;
        }

        public void MoveTowardPlayer(float dt)
        {
            if (_player == null) return;
            Vector3 dir = (_player.position - transform.position).normalized;
            dir.y = 0f;
            transform.position += dir * _moveSpeed * dt;
            if (dir != Vector3.zero)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(dir), 10f * dt);
        }

        public void OnDeath()
        {
            _spawner?.ClearAll();
            BossResultPortal.Instance?.OnBossDefeated();
            Debug.Log("[Greed] Boss 死亡，通關。");
        }

        // ════════════════════════════════════════════════════════════
        //  Animation Events
        // ════════════════════════════════════════════════════════════

        public void AnimEvent_AttackEnd() => OnAttackAnimEnd?.Invoke();
        public void AnimEvent_KickScaleEnd() => OnKickScaleAnimEnd?.Invoke();

        public void AnimEvent_EnableScaleHitbox()
        {
            if (_scaleHitbox) _scaleHitbox.enabled = true;
        }

        public void AnimEvent_DisableScaleHitbox()
        {
            if (_scaleHitbox) _scaleHitbox.enabled = false;
        }
    }
}