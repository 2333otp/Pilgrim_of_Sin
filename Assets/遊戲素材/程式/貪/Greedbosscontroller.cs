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
        [SerializeField] private float _balanceWindowDuration = 10f;       // 攻擊窗口秒數
        [SerializeField] private float _playerDamageBoostMultiplier = 1.5f; // 平衡時玩家傷害倍率
        [SerializeField] private float _bossAttackBoostMultiplier = 1.5f;   // 錢袋重時 Boss 攻擊倍率
        [SerializeField] private float _heavyBagDamageReduction = 0.15f;    // 錢袋重時玩家傷害乘數（高減傷）
        [SerializeField] private float _scaleKickDamage = 700f;             // 天秤踢翻傷害

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
        public ScalePhase CurrentPhase { get; private set; } = ScalePhase.StatueHeavy;
        public bool IsInBalanceWindow => _balanceWindowActive;

        // ── 動畫事件 ──────────────────────────────────────────────────
        public event Action OnAttackAnimEnd;
        public event Action OnKickScaleAnimEnd;

        // ── 內部 ──────────────────────────────────────────────────────
        private GreedBossStateMachine _fsm;
        private float _balanceWindowTimeRemaining;
        private bool _balanceWindowActive;
        private float _windowLogTimer;

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
            else
                Debug.LogError("[Greed] ❌ _scale 未設定！天秤機制無法運作。");

            if (_player == null)
                Debug.LogError("[Greed] ❌ _player 未設定！Boss 無法追蹤玩家。請在 Inspector 拖入 Player。");

            if (_spawner == null)
                Debug.LogError("[Greed] ❌ _spawner 未設定！錢袋無法生成。");

            Debug.Log($"[Greed] ✅ 初始化完成 — Phase={CurrentPhase}，HP={CurrentHp}，玩家距離={DistanceToPlayer:F1}");

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
            Debug.Log($"[Greed] 天秤重量更新：右側={rightWeight:F1}，Balanced={_scale.IsBalanced()}，RightHeavy={_scale.IsRightHeavy()}");

            if (CurrentPhase == ScalePhase.Kicked) return;

            if (_scale.IsBalanced())
            {
                if (!_balanceWindowActive)
                {
                    _balanceWindowActive = true;
                    _balanceWindowTimeRemaining = _balanceWindowDuration;
                    _windowLogTimer = 0f;
                    Debug.Log($"[Greed] ⚖️ 天秤平衡！10 秒攻擊窗口啟動，玩家傷害 ×{_playerDamageBoostMultiplier}，Boss 靜止。");
                }
                else
                {
                    Debug.Log($"[Greed] ⚖️ 再次平衡，從剩餘 {_balanceWindowTimeRemaining:F1} 秒繼續倒數。");
                }
                CurrentPhase = ScalePhase.Balanced;
            }
            else if (_scale.IsRightHeavy())
            {
                CurrentPhase = ScalePhase.MoneyBagHeavy;
                Debug.Log($"[Greed] 💰 錢袋重（右側={rightWeight:F1}），Boss 攻擊 ×{_bossAttackBoostMultiplier}，玩家高減傷。");
            }
            else
            {
                CurrentPhase = ScalePhase.StatueHeavy;
                Debug.Log($"[Greed] 🗿 雕像重（右側={rightWeight:F1}），雙方傷害正常，撿更多錢袋！");
            }
        }

        private void UpdateBalanceWindow(float dt)
        {
            if (!_balanceWindowActive) return;

            _balanceWindowTimeRemaining -= dt;

            _windowLogTimer -= dt;
            if (_windowLogTimer <= 0f)
            {
                _windowLogTimer = 2f;
                Debug.Log($"[Greed] ⏱ 攻擊窗口剩餘 {_balanceWindowTimeRemaining:F1} 秒，當前相位={CurrentPhase}");
            }

            if (_balanceWindowTimeRemaining <= 0f)
            {
                _balanceWindowActive = false;
                CurrentPhase = ScalePhase.Kicked;
                _fsm.Force(GreedBossStateType.KickScale);
                Debug.Log("[Greed] ⚡ 10 秒到，Boss 準備踢翻天秤！");
            }
        }

        // ════════════════════════════════════════════════════════════
        //  攻擊天秤重置窗口（由 ScaleObject.TakeDamage 呼叫）
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// 玩家攻擊天秤碰撞體時呼叫。
        /// 取消本輪攻擊窗口，計時器歸零，等待天秤下次進入平衡才重新計時。
        /// </summary>
        public void ResetBalanceWindow()
        {
            if (!_balanceWindowActive) return;
            _balanceWindowActive = false;
            _balanceWindowTimeRemaining = 0f;
            Debug.Log("[Greed] ⚡ 天秤被攻擊，攻擊窗口取消，等待下次平衡重新計時。");
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
            CurrentPhase = ScalePhase.StatueHeavy;
            _balanceWindowActive = false;
            _balanceWindowTimeRemaining = 0f;

            _scale?.ResetScale();
            _spawner?.SpawnCycle();

            Debug.Log("[Greed] 🔄 新一輪開始！天秤重置，錢袋重新生成，Boss 恢復追擊。");
        }

        // ════════════════════════════════════════════════════════════
        //  戰鬥介面
        // ════════════════════════════════════════════════════════════

        public void TakeDamage(float amount)
        {
            float actualDamage = CurrentPhase switch
            {
                ScalePhase.Balanced      => amount * _playerDamageBoostMultiplier,
                ScalePhase.MoneyBagHeavy => amount * _heavyBagDamageReduction,
                _                        => amount,
            };

            CurrentHp = Mathf.Max(0f, CurrentHp - actualDamage);
            Debug.Log($"[Greed] 受傷 {actualDamage:F1}（原始 {amount:F1}，相位 {CurrentPhase}），剩餘 HP {CurrentHp:F0}");

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
            float mult = CurrentPhase == ScalePhase.MoneyBagHeavy ? _bossAttackBoostMultiplier : 1f;
            return baseDmg * mult;
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