using System;
using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// Boss「癡」主控制器。
    /// 場地機制流程：
    ///   躲避階段（2輪 × 5秒善惡切換）→ 打Boss階段（10秒）→ 重複
    /// 惡區大小隨 Boss HP 線性縮小（HP百分比 = 惡區百分比）。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class FoolishBossController : MonoBehaviour, IDamageable
    {
        // ── References ───────────────────────────────────────────────
        [Header("References")]
        [SerializeField] private Transform _player;

        /// <summary>惡區物件（初始覆蓋整個地圖，依HP縮放）。</summary>
        [SerializeField] private EvilZone _evilZone;

        /// <summary>四個善區圓形物件。</summary>
        [SerializeField] private SafeZone[] _safeZones = new SafeZone[4];

        public Animator Animator { get; private set; }

        // ── Boss 基本數值 ─────────────────────────────────────────────
        [Header("Boss Stats")]
        [SerializeField] private float _maxHp = 10000f;
        [SerializeField] private float _moveSpeed = 3.5f;
        [SerializeField] private float _idleDuration = 1f;

        [Header("Attack Damage")]
        [SerializeField] private float _attack1Damage = 800f;
        [SerializeField] private float _attack2Damage = 1000f;
        [SerializeField] private float _attack3Damage = 1700f;

        [Header("Attack Range")]
        [SerializeField] private float _attack1Range = 3f;
        [SerializeField] private float _attack2Range = 6f;
        [SerializeField] private float _attack3Range = 10f;

        // ── 場地機制數值 ──────────────────────────────────────────────
        [Header("Phase Timing")]
        [SerializeField] private float _evadeRoundDuration = 5f;   // 每輪躲避秒數
        [SerializeField] private int _evadeRoundsPerCycle = 2;   // 每循環躲避輪數
        [SerializeField] private float _battleDuration = 10f; // 打Boss階段秒數

        // ── Stagger（選配） ───────────────────────────────────────────
        [Header("Stagger (Optional)")]
        [SerializeField] public bool enableStagger = false;
        [SerializeField] public float StaggerDuration = 0.5f;

        // ── 公開屬性 ─────────────────────────────────────────────────
        public float CurrentHp { get; private set; }
        public bool IsDead => CurrentHp <= 0f;
        public float IdleDuration => _idleDuration;
        public float Attack1Range => _attack1Range;
        public float Attack2Range => _attack2Range;
        public float Attack3Range => _attack3Range;
        public FoolishPhase CurrentPhase { get; private set; } = FoolishPhase.Evade;
        public float DistanceToPlayer => _player != null
                                         ? Vector3.Distance(transform.position, _player.position)
                                         : float.MaxValue;

        // ── 動畫事件 ─────────────────────────────────────────────────
        public event Action OnAttackAnimEnd;

        // ── 內部計時 ─────────────────────────────────────────────────
        private FoolishBossStateMachine _fsm;
        private float _phaseTimer;
        private int _evadeRoundCount; // 已完成的躲避輪數
        private bool _safeZoneActive;  // 當前善區是否顯示（閃爍用）

        // ════════════════════════════════════════════════════════════
        //  Unity 生命周期
        // ════════════════════════════════════════════════════════════

        private void Awake()
        {
            Animator = GetComponent<Animator>();
            CurrentHp = _maxHp;
            BuildFSM();
        }

        private void Start()
        {
            EnterEvadePhase();
        }

        private void Update()
        {
            UpdatePhase(Time.deltaTime);
            _fsm.Update(Time.deltaTime);
        }

        private void FixedUpdate() => _fsm.FixedUpdate(Time.fixedDeltaTime);

        // ════════════════════════════════════════════════════════════
        //  FSM 組裝
        // ════════════════════════════════════════════════════════════

        private void BuildFSM()
        {
            _fsm = new FoolishBossStateMachine();

            var states = new Dictionary<FoolishBossStateType, FoolishBossState>
            {
                { FoolishBossStateType.Idle,    new FoolishIdleState(this, _fsm)                                },
                { FoolishBossStateType.Move,    new FoolishMoveState(this, _fsm)                                },
                { FoolishBossStateType.Attack1, new FoolishAttackState(this, _fsm, FoolishBossStateType.Attack1)},
                { FoolishBossStateType.Attack2, new FoolishAttackState(this, _fsm, FoolishBossStateType.Attack2)},
                { FoolishBossStateType.Attack3, new FoolishAttackState(this, _fsm, FoolishBossStateType.Attack3)},
                { FoolishBossStateType.Stagger, new FoolishStaggerState(this, _fsm)                             },
                { FoolishBossStateType.Dead,    new FoolishDeadState(this, _fsm)                                },
            };

            _fsm.Init(states, FoolishBossStateType.Idle);
        }

        // ════════════════════════════════════════════════════════════
        //  場地相位管理
        // ════════════════════════════════════════════════════════════

        private void UpdatePhase(float dt)
        {
            _phaseTimer -= dt;
            if (_phaseTimer > 0f) return;

            if (CurrentPhase == FoolishPhase.Evade)
            {
                _evadeRoundCount++;
                Debug.Log($"[Foolish] 躲避第 {_evadeRoundCount} 輪結束");

                if (_evadeRoundCount >= _evadeRoundsPerCycle)
                    EnterBattlePhase();
                else
                    StartNextEvadeRound();
            }
            else // Battle
            {
                EnterEvadePhase();
            }
        }

        private void EnterEvadePhase()
        {
            CurrentPhase = FoolishPhase.Evade;
            _evadeRoundCount = 0;
            _phaseTimer = _evadeRoundDuration;

            SetSafeZonesVisible(true);
            UpdateEvilZoneSize();

            // Boss 進入 Idle（躲避階段不攻擊）
            _fsm.Request(FoolishBossStateType.Idle);
            Debug.Log("[Foolish] 進入躲避階段");
        }

        private void StartNextEvadeRound()
        {
            _phaseTimer = _evadeRoundDuration;
            // 切換善惡區顯示（閃爍機制）
            _safeZoneActive = !_safeZoneActive;
            SetSafeZonesVisible(_safeZoneActive);
            UpdateEvilZoneSize();
            Debug.Log($"[Foolish] 躲避第 {_evadeRoundCount + 1} 輪開始，善區={_safeZoneActive}");
        }

        private void EnterBattlePhase()
        {
            CurrentPhase = FoolishPhase.Battle;
            _phaseTimer = _battleDuration;

            // 打Boss階段：善惡區暫停，善區全開
            SetSafeZonesVisible(true);
            Debug.Log("[Foolish] 進入打Boss階段，10秒");
        }

        // ════════════════════════════════════════════════════════════
        //  惡區縮放（依 HP 百分比線性縮小）
        // ════════════════════════════════════════════════════════════

        private void UpdateEvilZoneSize()
        {
            if (_evilZone == null) return;
            float hpPercent = CurrentHp / _maxHp; // 1.0 → 0.0
            _evilZone.SetSizeByHpPercent(hpPercent);
        }

        private void SetSafeZonesVisible(bool visible)
        {
            foreach (var z in _safeZones)
                if (z != null) z.gameObject.SetActive(visible);
        }

        // ════════════════════════════════════════════════════════════
        //  戰鬥介面
        // ════════════════════════════════════════════════════════════

        public void TakeDamage(float amount)
        {
            // 躲避階段 Boss 無敵
            if (CurrentPhase == FoolishPhase.Evade)
            {
                Debug.Log("[Foolish] 躲避階段無敵，傷害無效。");
                return;
            }

            CurrentHp = Mathf.Max(0f, CurrentHp - amount);
            Debug.Log($"[Foolish] 受傷 {amount}，剩餘 HP {CurrentHp}");

            // 每次受傷後更新惡區大小
            UpdateEvilZoneSize();

            if (CurrentHp <= 0f)
            {
                _fsm.Force(FoolishBossStateType.Dead);
                return;
            }

            if (enableStagger)
                _fsm.Request(FoolishBossStateType.Stagger);
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

        public float GetAttackDamage(FoolishBossStateType type)
        {
            return type switch
            {
                FoolishBossStateType.Attack1 => _attack1Damage,
                FoolishBossStateType.Attack2 => _attack2Damage,
                _ => _attack3Damage,
            };
        }

        public void OnDeath()
        {
            SetSafeZonesVisible(true);
            if (_evilZone) _evilZone.gameObject.SetActive(false);
            Debug.Log("[Foolish] Boss 死亡，通關。");
        }

        // ════════════════════════════════════════════════════════════
        //  Animation Events
        // ════════════════════════════════════════════════════════════

        public void AnimEvent_AttackEnd() => OnAttackAnimEnd?.Invoke();
    }
}