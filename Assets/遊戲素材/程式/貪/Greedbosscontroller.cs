using System;
using System.Collections;
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
    public class GreedBossController : MonoBehaviour, IDamageable, IBossHealth
    {
        // ── References ───────────────────────────────────────────────
        [Header("References")]
        [SerializeField] private Transform _player;
        [SerializeField] private string _displayName = "心魔-貪";   // 血條旁名字（IBossHealth）
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
        [SerializeField] private float _scaleKickDamage = 700f;             // 天秤踢翻傷害（推給 ScaleHitbox）
        [SerializeField] private float _scaleKickHitboxDelay = 0.35f;       // Break 動畫踢擊幀延遲
        [SerializeField] private float _scaleKickHitboxDuration = 0.7f;     // 天秤傷害碰撞體開啟時長
        [SerializeField] private float _introDuration = 0.9f;               // 進場 CtoR 動畫長度，播完才開始戰鬥

        // ── Stagger（選配） ───────────────────────────────────────────
        [Header("Stagger (Optional)")]
        [SerializeField] public bool enableStagger = false;
        [SerializeField] public float StaggerDuration = 0.5f;

        // ── 公開屬性 ──────────────────────────────────────────────────
        public float CurrentHp { get; private set; }
        public float MaxHp => _maxHp;
        public bool IsDead => CurrentHp <= 0f;
        public string DisplayName => _displayName;   // IBossHealth
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
        public GreedBossStateType CurrentStateType => _fsm?.CurrentType ?? GreedBossStateType.Idle;

        // ── 動畫事件 ──────────────────────────────────────────────────
        public event Action OnAttackAnimEnd;
        public event Action OnKickScaleAnimEnd;

        // ── 內部 ──────────────────────────────────────────────────────
        private GreedBossStateMachine _fsm;
        private float _balanceWindowTimeRemaining;
        private bool _balanceWindowActive;
        private bool _gameplayStarted;   // 進場動畫播完前為 false，戰鬥/生成/天秤邏輯全部暫緩

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
            {
                _scale.OnWeightChanged += HandleScaleWeightChanged;
                _scale.OnBreakComplete += HandleScaleBreakComplete;
            }
            else
                Debug.LogError("[Greed] ❌ _scale 未設定！天秤機制無法運作。");

            if (_player == null)
                Debug.LogError("[Greed] ❌ _player 未設定！Boss 無法追蹤玩家。請在 Inspector 拖入 Player。");

            if (_spawner == null)
                Debug.LogError("[Greed] ❌ _spawner 未設定！錢袋無法生成。");

            // 進場：先讓天秤播完 CtoR（停右傾），再開始戰鬥與生成錢袋
            StartCoroutine(IntroRoutine());
        }

        private IEnumerator IntroRoutine()
        {
            yield return new WaitForSeconds(_introDuration);
            _gameplayStarted = true;
            _spawner?.SpawnCycle();
        }

        private void Update()
        {
            // 暫停時跳過（坑 #9）
            if (Time.timeScale == 0f) return;
            if (!_gameplayStarted) return; // 進場動畫期間不跑戰鬥/天秤邏輯

            UpdateBalanceWindow(Time.deltaTime);
            _fsm.Update(Time.deltaTime);
        }

        private void FixedUpdate()
        {
            if (Time.timeScale == 0f) return;
            if (!_gameplayStarted) return;
            _fsm.FixedUpdate(Time.fixedDeltaTime);
        }

        private void OnDestroy()
        {
            if (_scale != null)
            {
                _scale.OnWeightChanged -= HandleScaleWeightChanged;
                _scale.OnBreakComplete -= HandleScaleBreakComplete;
            }
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
            if (CurrentPhase == ScalePhase.Kicked) return;

            if (_scale.IsBalanced())
            {
                if (!_balanceWindowActive)
                {
                    _balanceWindowActive = true;
                    _balanceWindowTimeRemaining = _balanceWindowDuration;
                }
                CurrentPhase = ScalePhase.Balanced;
            }
            else if (_scale.IsRightHeavy())
            {
                // 超重：取消平衡窗口，天秤只維持傾斜、不自行打翻。
                // 要玩家主動攻擊天秤才會觸發重製（見 OnScaleHitWhileOverweight）。
                _balanceWindowActive = false;
                _balanceWindowTimeRemaining = 0f;
                CurrentPhase = ScalePhase.MoneyBagHeavy;
            }
            else
            {
                // 離開平衡回到雕像重，也取消窗口（沒維持平衡就不該倒數）。
                _balanceWindowActive = false;
                _balanceWindowTimeRemaining = 0f;
                CurrentPhase = ScalePhase.StatueHeavy;
            }
        }

        private void UpdateBalanceWindow(float dt)
        {
            if (!_balanceWindowActive) return;

            _balanceWindowTimeRemaining -= dt;

            if (_balanceWindowTimeRemaining <= 0f)
            {
                _balanceWindowActive = false;
                CurrentPhase = ScalePhase.Kicked;
                _fsm.Force(GreedBossStateType.KickScale);
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
        }

        // ════════════════════════════════════════════════════════════
        //  超重狀態下玩家攻擊天秤（由 ScaleObject.OnTriggerEnter 呼叫）
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// 超重（MoneyBagHeavy）時玩家攻擊天秤才會觸發。
        /// 目前先直接跑循環重製（打落錢袋、重生一批、相位回雕像重、傾斜靠 weight 歸零自然轉回），
        /// 沒有受擊演出。
        /// TODO(美術待補)：接「天秤受擊 + 重製」專屬動畫，改成動畫事件驅動 ResetScale()，
        ///                 不要沿用 Break 打翻動畫。
        /// </summary>
        public void OnScaleHitWhileOverweight()
        {
            if (CurrentPhase != ScalePhase.MoneyBagHeavy) return;
            ResetScale();
        }

        // ════════════════════════════════════════════════════════════
        //  踢翻天秤（由 GreedKickScaleState 呼叫）
        // ════════════════════════════════════════════════════════════

        /// <summary>KickScaleState 進入時呼叫：天秤播 Break 動畫，並在踢擊幀開啟天秤傷害碰撞體。</summary>
        public void PlayScaleBreak()
        {
            _scale?.PlayBreak();
            StartCoroutine(ScaleKickHitboxPulse());
        }

        private IEnumerator ScaleKickHitboxPulse()
        {
            if (_scaleHitbox == null) yield break;
            _scaleHitbox.GetComponent<ScaleHitbox>()?.SetDamage(_scaleKickDamage);
            yield return new WaitForSeconds(_scaleKickHitboxDelay);
            _scaleHitbox.enabled = true;
            yield return new WaitForSeconds(_scaleKickHitboxDuration);
            _scaleHitbox.enabled = false;
        }

        /// <summary>天秤 Break 動畫播完 → 當成 KickScale 動畫結束訊號。</summary>
        private void HandleScaleBreakComplete() => OnKickScaleAnimEnd?.Invoke();

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