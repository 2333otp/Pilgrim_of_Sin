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
    ///   - 驅動動畫（Animator 在子物件 Model 上，以單一 int 參數 "State" 切換，見 SetAnimState）
    ///   - 持有所有 Inspector 可調數值
    /// </summary>
    public class GreedBossController : MonoBehaviour, IDamageable, IBossHealth
    {
        // ── References ───────────────────────────────────────────────
        [Header("References")]
        [SerializeField] private Transform _player;
        [SerializeField] private string _displayName = "心魔-貪";   // 血條旁名字（IBossHealth）
        [SerializeField] private ScaleObject _scale;         // 天秤場景物件
        [SerializeField] private Collider _scaleHitbox;   // 天秤踢翻碰撞體
        [SerializeField] private MoneybagSpawner _spawner;       // 錢袋生成器

        /// <summary>骨架模型（子物件 Model）上的 Animator，不在本物件上，所以用 GetComponentInChildren 取得。</summary>
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
        [SerializeField] private float _balanceWindowDuration = 12f;       // 攻擊窗口秒數
        [SerializeField] private float _playerDamageBoostMultiplier = 3f;   // 平衡時玩家傷害倍率
        [SerializeField] private float _bossAttackBoostMultiplier = 1.5f;   // 錢袋重時 Boss 攻擊倍率
        [SerializeField] private float _heavyBagDamageReduction = 0.15f;    // 錢袋重時玩家傷害乘數（高減傷）
        [SerializeField] private float _statueHeavyDamageMultiplier = 0.4f; // 雕像重（錢袋還不夠平衡）時玩家傷害乘數
        [SerializeField] private float _scaleKickDamage = 700f;             // 天秤踢翻傷害（推給 ScaleHitbox）
        [SerializeField] private float _scaleKickHitboxDelay = 0.35f;       // Break 動畫踢擊幀延遲
        [SerializeField] private float _scaleKickHitboxDuration = 0.7f;     // 天秤傷害碰撞體開啟時長
        [SerializeField] private float _introDuration = 0.9f;               // 進場 CtoR 動畫長度，播完才開始戰鬥

        // ── 動畫 ──────────────────────────────────────────────────────
        [Header("Animation")]
        [Tooltip("Run 動畫本身對應的移動速度（m/s，由腳掌著地速度量測）。Run 播放速度 = _moveSpeed / 此值，避免滑步。")]
        [SerializeField] private float _runAnimReferenceSpeed = 4.5f;
        [Tooltip("Attack2/Attack3 的動畫還沒做。關閉時 Boss 一律追擊到 Attack1 範圍再出 Attack1；做好動畫後再打開。")]
        [SerializeField] private bool _useAttack2And3 = false;

        // ── 攻擊判定（Attack1 動畫事件 AnimEvent_AttackHit 觸發） ─────
        [Header("Attack Hit")]
        [Tooltip("判定球中心：Boss 前方距離（m）")]
        [SerializeField] private float _attackHitForward = 1.3f;
        [Tooltip("判定球中心：離地高度（m）")]
        [SerializeField] private float _attackHitHeight = 1.0f;
        [Tooltip("判定球半徑（m）")]
        [SerializeField] private float _attackHitRadius = 1.5f;
        [Tooltip("攻擊開始後多少秒內 Boss 持續轉向玩家（之後砸下去的方向就鎖定了）")]
        [SerializeField] private float _attackTrackDuration = 0.7f;

        // ── 血量閘門 ──────────────────────────────────────────────────
        [Header("HP Gate (非平衡期間的血量鎖)")]
        [Tooltip("把血量切成幾段。天秤不在平衡時，玩家的傷害最多只能把血打到「目前這一段的底線」，" +
                 "想打穿底線（包含最後的致命一擊）只能在天秤平衡的窗口內。0 = 關閉閘門。" +
                 "例：4 段 = 底線在 75% / 50% / 25% / 1 點血，需要撐過數次平衡窗口才能擊殺。")]
        [SerializeField, Min(0)] private int _hpGateCount = 0;

        // ── Stagger（選配） ───────────────────────────────────────────
        [Header("Stagger (Optional)")]
        [SerializeField] public bool enableStagger = false;
        [SerializeField] public float StaggerDuration = 0.63f;   // = Damage 動畫長度
        [Tooltip("兩次受擊硬直的最短間隔（秒）。避免連段把 Boss 鎖死在受擊動作；攻擊/踢天秤中不會被打斷（霸體）。")]
        [SerializeField] public float StaggerCooldown = 4f;

        // ── 公開屬性 ──────────────────────────────────────────────────
        public float CurrentHp { get; private set; }
        public float MaxHp => _maxHp;
        public bool IsDead => CurrentHp <= 0f;
        public string DisplayName => _displayName;   // IBossHealth
        public float IdleDuration => _idleDuration;
        public float Attack1Range => _attack1Range;
        public float Attack2Range => _attack2Range;
        public float Attack3Range => _attack3Range;
        public bool UseAttack2And3 => _useAttack2And3;
        public float AttackTrackDuration => _attackTrackDuration;
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
        private CapsuleCollider _capsule; // 供 MoveTowardPlayer() 做穿模檢查用
        private float _lastStaggerTime = float.NegativeInfinity;
        private float _blockedTime;      // 追擊時連續「想走卻走不動」的秒數，超過門檻動畫就退回 Idle（避免原地空跑）
        private readonly RaycastHit[] _castHits = new RaycastHit[16];   // ClampStepToAvoidObstacles 的掃描結果緩衝（避免每幀配置記憶體）
        private readonly Collider[] _overlaps = new Collider[16];       // DepenetrateFromObstacles 的重疊結果緩衝
        private GreedBossNavigator _navigator;   // 追擊路徑規劃（繞過天秤與錢袋）
        private Vector3 _slideDir;       // 被擋住時選定的繞行方向（只要還走得通就一直沿用，避免左右抖動）
        private const float BlockedAnimDelay = 0.25f;

        // Animator 參數（int）。所有狀態都由這一個參數驅動，不用 Trigger：
        // Trigger 在沒有可消耗的轉換時會殘留，之後在錯誤時機觸發，造成動畫打架。
        private static readonly int StateHash = Animator.StringToHash("State");
        private static readonly int MoveSpeedHash = Animator.StringToHash("MoveSpeed");
        private const int AnimIdle = 0, AnimMove = 1, AnimAttack1 = 2, AnimStagger = 3, AnimDead = 4;

        // ════════════════════════════════════════════════════════════
        //  Unity 生命週期
        // ════════════════════════════════════════════════════════════

        private void Awake()
        {
            Animator = GetComponentInChildren<Animator>(true);
            if (Animator == null)
                Debug.LogError("[Greed] ❌ 找不到 Animator！骨架模型（子物件 Model）必須帶 Animator。");
            else
            {
                // 位移完全由 MoveTowardPlayer() 控制，動畫不可帶動 Boss 本體，否則兩邊會打架。
                Animator.applyRootMotion = false;
                Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // 離開畫面也要跑，否則 AnimEvent 不會觸發、攻擊流程卡住
                Animator.SetFloat(MoveSpeedHash, _runAnimReferenceSpeed > 0f ? _moveSpeed / _runAnimReferenceSpeed : 1f);
            }
            _capsule = GetComponent<CapsuleCollider>();
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
                _scale.OnHitResetComplete += HandleScaleHitResetComplete;
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
            EnsureFSM();

            // 玩家死了（失敗流程進行中）就停手：不追、不攻擊、天秤邏輯也暫停。
            if (BossResultPortal.IsPlayerDefeated) { FreezeForPlayerDefeat(); return; }

            // 暫停時跳過（坑 #9）
            if (Time.timeScale == 0f) return;
            if (!_gameplayStarted) return; // 進場動畫期間不跑戰鬥/天秤邏輯

            UpdateBalanceWindow(Time.deltaTime);
            _fsm.Update(Time.deltaTime);
        }

        private void FixedUpdate()
        {
            EnsureFSM();

            if (BossResultPortal.IsPlayerDefeated) return;

            if (Time.timeScale == 0f) return;
            if (!_gameplayStarted) return;
            _fsm.FixedUpdate(Time.fixedDeltaTime);
        }

        private bool _frozenForPlayerDefeat;

        /// <summary>玩家死亡後 Boss 站定（Idle 姿勢），關掉天秤踢擊碰撞體，不再傷害玩家。只做一次。</summary>
        private void FreezeForPlayerDefeat()
        {
            if (_frozenForPlayerDefeat) return;
            _frozenForPlayerDefeat = true;

            StopAllCoroutines();                       // 結束進行中的天秤踢擊碰撞體脈衝
            if (_scaleHitbox) _scaleHitbox.enabled = false;
            SetAnimState(GreedBossStateType.Idle);
        }

        /// <summary>
        /// 同 PlayerController.EnsureStateMachine() 的坑：Play Mode 途中若剛好發生 Domain Reload，
        /// Unity 不會對場景裡已存在的物件重新呼叫 Awake()，_fsm 這種純 C# 物件（非 MonoBehaviour、
        /// 沒有被序列化）會被清空成 null，Update()/FixedUpdate() 因此每幀丟 NullReferenceException，
        /// Boss 卡死不動。這裡偵測到 null 就重建一次自救，代價是重建後回到 Idle。
        /// </summary>
        private void EnsureFSM()
        {
            if (_fsm != null) return;
            Debug.LogWarning("[GreedBossController] _fsm 是 null（可能是 Play Mode 中途發生了 Domain Reload），重新建立狀態機。");
            BuildFSM();
        }

        private void OnDestroy()
        {
            if (_scale != null)
            {
                _scale.OnWeightChanged -= HandleScaleWeightChanged;
                _scale.OnBreakComplete -= HandleScaleBreakComplete;
                _scale.OnHitResetComplete -= HandleScaleHitResetComplete;
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
                // 累積攻擊天秤達門檻才會觸發重製（見 OnScaleAttacked）。
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
        //  循環重置（由 KickScaleState 動畫結束後呼叫）
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// KickScaleState 動畫結束後呼叫。
        /// 重置天秤重量，清除舊錢袋，生成新一批錢袋。
        /// </summary>
        public void ResetScale()
        {
            if (IsDead) return; // 死亡後天秤動畫才播完的話，不要再重生錢袋
            CurrentPhase = ScalePhase.StatueHeavy;
            _balanceWindowActive = false;
            _balanceWindowTimeRemaining = 0f;

            _scale?.ResetScale();
            _spawner?.SpawnCycle();
        }

        // ════════════════════════════════════════════════════════════
        //  玩家攻擊天秤累積達門檻（由 ScaleObject.OnTriggerEnter 呼叫）
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// 玩家攻擊天秤累積次數達到 GreedScaleConfig.HitsRequiredToTrigger 時呼叫，
        /// 不論當下相位（雕像重/平衡/錢袋重）。
        /// 播受擊+重製動畫（Attacked → MoneyBagUpdate），動畫播完由 HandleScaleHitResetComplete
        /// 呼叫 ResetScale()（打落錢袋、重生一批、相位回雕像重）。
        /// </summary>
        public void OnScaleAttacked()
        {
            _scale?.PlayHitReset();
        }

        /// <summary>天秤受擊+重製動畫播完 → 執行實際的重製邏輯。</summary>
        private void HandleScaleHitResetComplete() => ResetScale();

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
                ScalePhase.StatueHeavy   => amount * _statueHeavyDamageMultiplier,
                ScalePhase.MoneyBagHeavy => amount * _heavyBagDamageReduction,
                _                        => amount,
            };

            float newHp = CurrentHp - actualDamage;
            if (CurrentPhase != ScalePhase.Balanced)
                newHp = Mathf.Max(newHp, GetGateFloor(CurrentHp));   // 非平衡：打不穿這一段的底線
            CurrentHp = Mathf.Max(0f, newHp);

            if (CurrentHp <= 0f)
            {
                _fsm.Force(GreedBossStateType.Dead);
                return;
            }

            // 只在待機/移動時硬直，攻擊與踢天秤中是霸體；加冷卻避免連段把 Boss 鎖死。
            if (enableStagger
                && Time.time - _lastStaggerTime >= StaggerCooldown
                && (CurrentStateType == GreedBossStateType.Idle || CurrentStateType == GreedBossStateType.Move))
            {
                _lastStaggerTime = Time.time;
                _fsm.Request(GreedBossStateType.Stagger);
            }
        }

        /// <summary>
        /// 目前血量所在那一段的底線（非平衡時血量不能低於它）。
        /// 底線 = { 1, 每一段的邊界 (最大血量 × k / 段數, k = 1..段數-1) } 裡「不高於目前血量」的最大者。
        /// 剛好停在邊界上時底線就是邊界本身，所以不會因為落在邊界而多穿一段。
        /// </summary>
        private float GetGateFloor(float hp)
        {
            if (_hpGateCount <= 0) return 0f;

            float segment = _maxHp / _hpGateCount;
            float floor = 1f;
            for (int k = 1; k < _hpGateCount; k++)
            {
                float boundary = segment * k;
                if (boundary <= hp) floor = boundary;
            }
            return Mathf.Min(floor, hp);
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

        // Boss capsule半徑0.5 + Player box collider半寬0.5，留一點緩衝，避免兩者實體重疊。
        private const float MinDistanceToPlayer = 1.1f;

        /// <summary>水平轉向玩家（Idle 待機與攻擊前搖用，Move 自己有轉向）。</summary>
        public void FacePlayer(float dt, float turnSpeed = 10f)
        {
            if (_player == null) return;
            Vector3 dir = _player.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(dir.normalized), turnSpeed * dt);
        }

        /// <summary>
        /// 依 FSM 狀態切換動畫。目前沒有專屬動畫的狀態：
        ///   Attack2/Attack3 → 暫用 Attack1 動畫（預設已關閉，見 _useAttack2And3）
        ///   KickScale       → 維持 Idle（踢天秤動畫還沒做，天秤自己會播 Break）
        /// </summary>
        public void SetAnimState(GreedBossStateType type)
        {
            if (Animator == null || Animator.runtimeAnimatorController == null) return;
            int value = type switch
            {
                GreedBossStateType.Move    => AnimMove,
                GreedBossStateType.Attack1 => AnimAttack1,
                GreedBossStateType.Attack2 => AnimAttack1,
                GreedBossStateType.Attack3 => AnimAttack1,
                GreedBossStateType.Stagger => AnimStagger,
                GreedBossStateType.Dead    => AnimDead,
                _                          => AnimIdle,
            };
            if (type == GreedBossStateType.Move) _blockedTime = 0f;
            Animator.SetInteger(StateHash, value);
        }

        /// <summary>追擊中實際有沒有在移動：被擋住超過門檻就改播 Idle，恢復移動再播 Run，動畫才不會和位移打架。</summary>
        private void UpdateMoveAnim(bool moved, float dt)
        {
            if (Animator == null || Animator.runtimeAnimatorController == null) return;
            _blockedTime = moved ? 0f : _blockedTime + dt;
            Animator.SetInteger(StateHash, _blockedTime < BlockedAnimDelay ? AnimMove : AnimIdle);
        }

        public void MoveTowardPlayer(float dt)
        {
            if (_player == null) return;

            // 往哪走：直線走得通就朝玩家；被天秤/錢袋擋住就沿規劃出的路點繞過去。
            if (_navigator == null && _capsule != null)
                _navigator = new GreedBossNavigator(_capsule.radius, ObstacleMask);
            Vector3 target = _navigator != null
                ? _navigator.GetSteerTarget(transform.position, _player.position)
                : _player.position;

            Vector3 dir = target - transform.position;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.zero;

            float step = _moveSpeed * dt;
            Vector3 moveDir = dir;
            if (dir != Vector3.zero && step > 0f)
            {
                float allowed = ClampStepToAvoidObstacles(dir, step, out Vector3 blockNormal);

                // 被障礙物（例如滾到路上的錢袋、天秤）擋住：換個方向繞過去，否則 Boss 會永遠卡在原地。
                if (allowed < step * 0.5f && blockNormal != Vector3.zero)
                {
                    Vector3 chosen = PickDetourDirection(dir, step, blockNormal, out float chosenAllowed);
                    if (chosen != Vector3.zero && chosenAllowed > allowed)
                    {
                        moveDir = chosen;
                        allowed = chosenAllowed;
                        _slideDir = chosen;
                    }
                }
                else
                    _slideDir = Vector3.zero;   // 正路通了，下次被擋再重新選

                step = ClampStepToAvoidPlayer(allowed);
            }

            transform.position += moveDir * step;
            DepenetrateFromObstacles();
            if (dir != Vector3.zero)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(dir), 10f * dt);

            UpdateMoveAnim(step > _moveSpeed * dt * 0.1f, dt);
        }

        /// <summary>
        /// 正路被擋時挑一個繞行方向。優先沿用上一次選的（還走得通就不換，避免兩側都有障礙時左右抖動），
        /// 否則依序試：沿障礙面的兩個切線方向（偏向玩家那側優先）→ 往後 ±135° → 直接後退。
        /// 夾在兩個障礙物中間（例如天秤跟錢袋之間）時，只有後退那幾個方向走得通，靠這個脫困。
        /// </summary>
        private Vector3 PickDetourDirection(Vector3 dir, float step, Vector3 blockNormal, out float bestAllowed)
        {
            float good = step * 0.5f;
            bestAllowed = 0f;
            Vector3 best = Vector3.zero;

            if (_slideDir != Vector3.zero)
            {
                float a = ClampStepToAvoidObstacles(_slideDir, step, out _);
                if (a >= good) { bestAllowed = a; return _slideDir; }
            }

            Vector3 tangent = Vector3.Cross(Vector3.up, blockNormal).normalized;
            if (Vector3.Dot(tangent, dir) < 0f) tangent = -tangent;   // 偏向玩家那側的切線排第一

            Vector3[] candidates =
            {
                tangent,
                -tangent,
                Quaternion.Euler(0f, 135f, 0f) * dir,
                Quaternion.Euler(0f, -135f, 0f) * dir,
                -dir,
            };

            foreach (var c in candidates)
            {
                Vector3 d = c; d.y = 0f; d.Normalize();
                float a = ClampStepToAvoidObstacles(d, step, out _);
                if (a >= good) { bestAllowed = a; return d; }
                if (a > bestAllowed) { bestAllowed = a; best = d; }
            }
            return best;
        }

        /// <summary>
        /// Boss 的 Rigidbody 是 kinematic（避免被玩家碰撞推走，見「蹬到 Boss 身上會位移」修正），
        /// 但 kinematic 不會被 Unity 物理自動擋出場景固定物件（例如天秤）——直線朝玩家移動時
        /// 會直接穿模走進去。這裡用 CapsuleCast 掃描下一步是否會撞進場景固定碰撞體，撞到就把
        /// 這一步的距離縮到碰撞點前，不再穿模。忽略觸發器、自己所在的敵人層、以及玩家層
        /// （撞到玩家該是物理推擠玩家，不該卡住 Boss 的追擊路徑）。
        /// </summary>
        private float ClampStepToAvoidObstacles(Vector3 dir, float step, out Vector3 blockNormal)
        {
            blockNormal = Vector3.zero;
            if (_capsule == null) return step;

            GetCapsuleWorldPoints(out Vector3 p1, out Vector3 p2, out float radius);
            int count = Physics.CapsuleCastNonAlloc(p1, p2, radius, dir, _castHits, step, ObstacleMask, QueryTriggerInteraction.Ignore);

            // 在所有命中裡挑「真的會撞上」的最近一個：
            //  · 起點已重疊（distance = 0）時 Unity 給的法線是無意義的，改用 ComputePenetration 算出「往哪邊推出去」；
            //    朝著障礙物裡面走才算擋住，往外或平行走就放行，Boss 才能脫離重疊（不會被自己的防穿模反鎖）。
            //  · 起點沒重疊時，只有「正在靠近」那個面的才算擋；貼著表面平行/遠離不算（沿牆繞行才走得動）。
            float bestDist = float.MaxValue;
            Vector3 bestNormal = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = _castHits[i];
                Vector3 n;
                if (h.distance <= 0f)
                {
                    if (!TryGetPushOut(h.collider, transform.position, out n, out _)) continue;
                    n.y = 0f;
                    if (n.sqrMagnitude < 0.0001f) continue;
                    n.Normalize();
                    if (Vector3.Dot(dir, n) >= -0.01f) continue;
                }
                else
                {
                    if (Vector3.Dot(dir, h.normal) >= -0.01f) continue;
                    n = h.normal; n.y = 0f;
                    n = n.sqrMagnitude > 0.0001f ? n.normalized : Vector3.zero;
                }

                if (h.distance < bestDist) { bestDist = h.distance; bestNormal = n; }
            }

            if (bestDist == float.MaxValue) return step;
            blockNormal = bestNormal;
            return Mathf.Max(0f, bestDist - 0.05f);
        }

        private int ObstacleMask
        {
            get
            {
                int mask = ~(1 << gameObject.layer);
                int playerLayer = LayerMask.NameToLayer("Player");
                if (playerLayer >= 0) mask &= ~(1 << playerLayer);
                return mask;
            }
        }

        private void GetCapsuleWorldPoints(out Vector3 p1, out Vector3 p2, out float radius)
        {
            radius = _capsule.radius;
            float halfLine = Mathf.Max(0f, _capsule.height * 0.5f - radius);
            Vector3 center = transform.position + _capsule.center;
            p1 = center + Vector3.up * halfLine;
            p2 = center - Vector3.up * halfLine;
        }

        /// <summary>算出 Boss 要往哪個方向、推多遠才能離開 other（僅支援基本形狀與 convex MeshCollider）。</summary>
        private bool TryGetPushOut(Collider other, Vector3 bossPos, out Vector3 pushDir, out float pushDist)
        {
            pushDir = Vector3.zero;
            pushDist = 0f;
            if (other == null || other.isTrigger) return false;
            if (other is MeshCollider mc && !mc.convex) return false;
            return Physics.ComputePenetration(_capsule, bossPos, transform.rotation,
                                              other, other.transform.position, other.transform.rotation,
                                              out pushDir, out pushDist);
        }

        /// <summary>
        /// 保險機制：每次移動後，如果還是嵌進固定物或錢袋（掃描的誤差、錢袋滾到身上、傳送等），
        /// 就水平推出去。這是「走進去之後整步放行」造成穿天秤的根本防線——就算哪一步判斷失誤，
        /// 下一個物理步也會被推回表面外，不會一路穿過去。只做水平推擠，不碰地板（豎直方向）。
        /// </summary>
        private void DepenetrateFromObstacles()
        {
            if (_capsule == null) return;

            for (int iter = 0; iter < 3; iter++)
            {
                GetCapsuleWorldPoints(out Vector3 p1, out Vector3 p2, out float radius);
                int count = Physics.OverlapCapsuleNonAlloc(p1, p2, radius, _overlaps, ObstacleMask, QueryTriggerInteraction.Ignore);

                bool moved = false;
                for (int i = 0; i < count; i++)
                {
                    if (!TryGetPushOut(_overlaps[i], transform.position, out Vector3 dir, out float dist)) continue;
                    Vector3 push = new Vector3(dir.x, 0f, dir.z);
                    if (push.sqrMagnitude < 0.04f) continue;   // 幾乎是豎直方向（地板、頂面）：不處理
                    transform.position += push * dist;
                    moved = true;
                }
                if (!moved) break;
            }
        }

        /// <summary>
        /// ClampStepToAvoidObstacles() 故意忽略玩家層，讓 Boss 追擊時不會被玩家卡住路徑；
        /// 但這代表玩家如果剛好站在 Boss 跟場景固定物件（例如天秤）中間，Boss 這個kinematic
        /// 直接瞬移的移動方式會完全不管玩家在不在，每一幀硬擠過來，把玩家夾著往固定物件裡推——
        /// 這才是玩家使用連段/攻擊動作時「跑進天秤裡」的真正根因，跟攻擊動畫的 root motion 無關
        /// （已個別測過全部4種武器共24種攻擊動作的 root motion，單獨測試都沒有穿透）。
        /// 這裡另外擋一層：不管上面那條擋不擋固定物件，Boss 這一步都不能把自己跟玩家的水平距離
        /// 縮到小於兩者碰撞體半徑總和以內，避免物理擠壓把玩家推穿場景固定物件。
        /// </summary>
        private float ClampStepToAvoidPlayer(float step)
        {
            if (_player == null || _capsule == null) return step;

            Vector3 flatBossPos = transform.position; flatBossPos.y = 0f;
            Vector3 flatPlayerPos = _player.position; flatPlayerPos.y = 0f;
            float currentDist = Vector3.Distance(flatBossPos, flatPlayerPos);
            float allowedStep = Mathf.Max(0f, currentDist - MinDistanceToPlayer);

            return Mathf.Min(step, allowedStep);
        }

        public void OnDeath()
        {
            StopAllCoroutines();                       // 結束進行中的天秤踢擊碰撞體脈衝
            if (_scaleHitbox) _scaleHitbox.enabled = false;
            if (_capsule) _capsule.enabled = false;    // 屍體不再擋路、不再被打
            _spawner?.ClearAll();
            BossResultPortal.Instance?.OnBossDefeated();
        }

        // ════════════════════════════════════════════════════════════
        //  Animation Events
        // ════════════════════════════════════════════════════════════

        public void AnimEvent_AttackEnd() => OnAttackAnimEnd?.Invoke();
        public void AnimEvent_KickScaleEnd() => OnKickScaleAnimEnd?.Invoke();

        /// <summary>
        /// Attack1 砸下去的那一幀（由 Model 上的 GreedBossAnimationEvents 轉發）。
        /// 在 Boss 前方做球形判定，命中玩家就扣血（玩家翻滾無敵幀由 PlayerController.TakeDamage 自己擋）。
        /// 若事件到達時 Boss 已離開攻擊狀態或已死亡就忽略，避免遲到的事件誤傷。
        /// </summary>
        public void AnimEvent_AttackHit()
        {
            if (IsDead || BossResultPortal.IsPlayerDefeated) return;
            var type = CurrentStateType;
            if (type != GreedBossStateType.Attack1 && type != GreedBossStateType.Attack2 && type != GreedBossStateType.Attack3)
                return;

            Vector3 center = transform.position + transform.forward * _attackHitForward + Vector3.up * _attackHitHeight;
            foreach (var col in Physics.OverlapSphere(center, _attackHitRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!col.CompareTag("Player")) continue;
                var player = col.GetComponentInParent<PlayerController>();
                if (player == null) continue;
                player.TakeDamage(GetAttackDamage(type));
                return; // 一次攻擊只打一次
            }
        }

        public void AnimEvent_EnableScaleHitbox()
        {
            if (_scaleHitbox) _scaleHitbox.enabled = true;
        }

        public void AnimEvent_DisableScaleHitbox()
        {
            if (_scaleHitbox) _scaleHitbox.enabled = false;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.6f);
            Gizmos.DrawWireSphere(transform.position + transform.forward * _attackHitForward + Vector3.up * _attackHitHeight, _attackHitRadius);
        }
    }
}