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
    ///   - 持有所有 Inspector 可調數值
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class GreedBossController : MonoBehaviour, IDamageable
    {
        // ── References ───────────────────────────────────────────────
        [Header("References")]
        [SerializeField] private Transform _player;
        [SerializeField] private ScaleObject _scale;      // 天秤場景物件
        [SerializeField] private Collider _scaleHitbox;// 天秤踢翻碰撞體

        public Animator Animator { get; private set; }

        // ── Boss 基本數值 ─────────────────────────────────────────────
        [Header("Boss Stats")]
        [SerializeField] private float _maxHp = 12000f;
        [SerializeField] private float _moveSpeed = 4f;
        [SerializeField] private float _idleDuration = 1f;   // Idle 待機秒數

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
        [SerializeField] private float _scaleBalanceThreshold = 25f;  // 天秤平衡閾值（最高50）
        [SerializeField] private float _attackBoostMultiplier = 1.5f; // 天秤傾斜時攻擊倍率
        [SerializeField] private float _scaleKickDamage = 700f; // 天秤踢翻傷害

        [Header("Moneybag")]
        [SerializeField] private int _moneybagCountMin = 1;
        [SerializeField] private int _moneybagCountMax = 10;
        [SerializeField] private float _moneybagWeightMin = 2f;
        [SerializeField] private float _moneybagWeightMax = 10f;

        // ── Stagger（選配） ───────────────────────────────────────────
        [Header("Stagger (Optional)")]
        [SerializeField] public bool enableStagger = false;
        [SerializeField] public float StaggerDuration = 0.5f;

        // ── 公開屬性（各狀態讀取） ────────────────────────────────────
        public float CurrentHp { get; private set; }
        public bool IsDead => CurrentHp <= 0f;
        public float IdleDuration => _idleDuration;
        public float Attack1Range => _attack1Range;
        public float Attack2Range => _attack2Range;
        public float Attack3Range => _attack3Range;
        public float DistanceToPlayer => _player != null
                                         ? Vector3.Distance(transform.position, _player.position)
                                         : float.MaxValue;

        // 天秤相位
        public ScalePhase CurrentPhase { get; private set; } = ScalePhase.Unbalanced;

        // ── 動畫事件 ─────────────────────────────────────────────────
        public event Action OnAttackAnimEnd;
        public event Action OnKickScaleAnimEnd;

        // ── 內部 ─────────────────────────────────────────────────────
        private GreedBossStateMachine _fsm;
        private float _balanceTimer;   // 10 秒攻擊窗口倒計時
        private float _currentAttackMultiplier = 1f;

        // ════════════════════════════════════════════════════════════
        //  Unity 生命周期
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

            SpawnMoneybags();
        }

        private void Update()
        {
            UpdateBalanceWindow(Time.deltaTime);
            _fsm.Update(Time.deltaTime);
        }

        private void FixedUpdate() => _fsm.FixedUpdate(Time.fixedDeltaTime);

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
                { GreedBossStateType.Idle,      new GreedIdleState(this, _fsm)                              },
                { GreedBossStateType.Move,      new GreedMoveState(this, _fsm)                              },
                { GreedBossStateType.Attack1,   new GreedAttackState(this, _fsm, GreedBossStateType.Attack1)},
                { GreedBossStateType.Attack2,   new GreedAttackState(this, _fsm, GreedBossStateType.Attack2)},
                { GreedBossStateType.Attack3,   new GreedAttackState(this, _fsm, GreedBossStateType.Attack3)},
                { GreedBossStateType.KickScale, new GreedKickScaleState(this, _fsm)                        },
                { GreedBossStateType.Stagger,   new GreedStaggerState(this, _fsm)                          },
                { GreedBossStateType.Dead,      new GreedDeadState(this, _fsm)                             },
            };

            _fsm.Init(states, GreedBossStateType.Idle);
        }

        // ════════════════════════════════════════════════════════════
        //  天秤相位管理
        // ════════════════════════════════════════════════════════════

        /// <summary>天秤重量變化時由 ScaleObject 呼叫。</summary>
        private void HandleScaleWeightChanged(float weightDifference)
        {
            if (CurrentPhase == ScalePhase.Kicked) return;

            bool balanced = Mathf.Abs(weightDifference) <= _scaleBalanceThreshold;

            if (balanced && CurrentPhase == ScalePhase.Unbalanced)
                EnterBalancedPhase();
            else if (!balanced && CurrentPhase == ScalePhase.Balanced)
                EnterUnbalancedPhase();
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

        /// <summary>KickScaleState 動畫結束後呼叫，重置天秤。</summary>
        public void ResetScale()
        {
            CurrentPhase = ScalePhase.Unbalanced;
            _currentAttackMultiplier = _attackBoostMultiplier;
            _scale?.ResetScale();
            SpawnMoneybags();
            Debug.Log("[Greed] 天秤重置，錢袋重新生成。");
        }

        // ════════════════════════════════════════════════════════════
        //  戰鬥介面
        // ════════════════════════════════════════════════════════════

        /// <summary>玩家攻擊命中 Boss 時呼叫。</summary>
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

            // Stagger 選配
            if (enableStagger)
                _fsm.Request(GreedBossStateType.Stagger);
        }

        /// <summary>取得當前實際攻擊傷害（天秤傾斜時乘以係數）。</summary>
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

        /// <summary>移動朝向玩家（由 MoveState.FixedUpdate 呼叫）。</summary>
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

        /// <summary>死亡流程（由 DeadState 呼叫）。</summary>
        public void OnDeath()
        {
            // TODO: 觸發通關演出
            Debug.Log("[Greed] Boss 死亡，通關。");
        }

        // ════════════════════════════════════════════════════════════
        //  錢袋
        // ════════════════════════════════════════════════════════════

        private void SpawnMoneybags()
        {
            int count = UnityEngine.Random.Range(_moneybagCountMin, _moneybagCountMax + 1);
            for (int i = 0; i < count; i++)
            {
                float weight = UnityEngine.Random.Range(_moneybagWeightMin, _moneybagWeightMax);
                _scale?.SpawnMoneybag(weight);
            }
            Debug.Log($"[Greed] 生成 {count} 個錢袋。");
        }

        // ════════════════════════════════════════════════════════════
        //  Animation Events（掛在 Animator 的動畫事件上）
        // ════════════════════════════════════════════════════════════

        public void AnimEvent_AttackEnd() => OnAttackAnimEnd?.Invoke();
        public void AnimEvent_KickScaleEnd() => OnKickScaleAnimEnd?.Invoke();

        /// <summary>踢翻動畫的特定幀：啟用天秤碰撞體造成傷害。</summary>
        public void AnimEvent_EnableScaleHitbox()
        {
            if (_scaleHitbox) _scaleHitbox.enabled = true;
        }

        /// <summary>踢翻動畫結束後：關閉天秤碰撞體。</summary>
        public void AnimEvent_DisableScaleHitbox()
        {
            if (_scaleHitbox) _scaleHitbox.enabled = false;
        }
    }
}