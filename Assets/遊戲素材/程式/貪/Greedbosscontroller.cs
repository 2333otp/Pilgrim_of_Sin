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
        [Header("素材總表（數值、動畫、音效、配音、特效、招式、派生、空襲都在這裡改）")]
        [Tooltip("Assets/遊戲素材/SO/Boss/GreedBossProfile。格式和癡的 FoolishBossProfile 相同（共通部分見 BossProfileBase）。")]
        [SerializeField] private GreedBossProfile _profile;

        [Header("References")]
        [SerializeField] private Transform _player;
        [SerializeField] private ScaleObject _scale;         // 天秤場景物件
        [SerializeField] private Collider _scaleHitbox;   // 天秤踢翻碰撞體
        [SerializeField] private MoneybagSpawner _spawner;       // 錢袋生成器

        /// <summary>骨架模型（子物件 Model）上的 Animator，不在本物件上，所以用 GetComponentInChildren 取得。</summary>
        public Animator Animator { get; private set; }

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
        [Tooltip("Run 動畫本身對應的移動速度（m/s，由腳掌著地速度量測）。Run 播放速度 = 素材總表的移動速度 / 此值，避免滑步。")]
        [SerializeField] private float _runAnimReferenceSpeed = 4.5f;

        // ── 血量閘門 ──────────────────────────────────────────────────
        [Header("HP Gate (非平衡期間的血量鎖)")]
        [Tooltip("把血量切成幾段。天秤不在平衡時，玩家的傷害最多只能把血打到「目前這一段的底線」，" +
                 "想打穿底線（包含最後的致命一擊）只能在天秤平衡的窗口內。0 = 關閉閘門。" +
                 "例：4 段 = 底線在 75% / 50% / 25% / 1 點血，需要撐過數次平衡窗口才能擊殺。")]
        [SerializeField, Min(0)] private int _hpGateCount = 0;

        // ── 公開屬性 ──────────────────────────────────────────────────
        public float CurrentHp { get; private set; }
        public float MaxHp => _profile.maxHp;
        public bool IsDead => CurrentHp <= 0f;
        public string DisplayName => _profile.displayName;   // IBossHealth
        public float IdleDuration => _profile.idleDuration;
        public float StaggerDuration => _profile.StaggerTime;
        public GreedBossProfile Profile => _profile;
        /// <summary>和主角的水平距離（Boss 飛在空中時也不受高度影響）。</summary>
        public float DistanceToPlayer
        {
            get
            {
                if (_player == null) return float.MaxValue;
                Vector3 d = _player.position - transform.position; d.y = 0f;
                return d.magnitude;
            }
        }
        public float HpRatio => MaxHp > 0f ? CurrentHp / MaxHp : 0f;
        public float GroundY => _groundY;

        private GreedFxPlayer _feedback;
        private float _nextHurtVoiceTime;
        /// <summary>音效 / 配音 / 特效播放（素材總表裡每個動作的音效 / 配音 / 特效，依動畫進度觸發）。</summary>
        public GreedFxPlayer Feedback => _feedback ??= new GreedFxPlayer(transform, () => _profile != null ? _profile.mixerGroup : null);   // 延遲建立：Domain Reload 後也會自動重建

        /// <summary>下一次進入 Action 狀態要播的招式（Idle 選招或派生時設定）。</summary>
        public GreedMoveEntry PendingMove { get; set; }

        /// <summary>除錯面板顯示用：目前招式 / 空襲階段。</summary>
        public string DebugDetail
        {
            get
            {
                if (_fsm?.Current is GreedActionState a && a.CurrentMove != null) return $"[{a.CurrentMove.stateName} {a.Progress:P0}]";
                if (_fsm?.Current is GreedKickScaleState k) return $"[{k.PhaseName}]";
                return "";
            }
        }

        // ── 天秤相位 ──────────────────────────────────────────────────
        public ScalePhase CurrentPhase { get; private set; } = ScalePhase.StatueHeavy;
        public bool IsInBalanceWindow => _balanceWindowActive;
        public GreedBossStateType CurrentStateType => _fsm?.CurrentType ?? GreedBossStateType.Idle;

        // ── 動畫事件 ──────────────────────────────────────────────────
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

        private float _groundY;                  // 地面高度（Awake 時 Boss 站的高度），空襲落地用
        private bool _airborne;
        private float _nextIdleDuration = -1f;   // < 0 = 用素材總表的待機時間
        private readonly Dictionary<GreedMoveEntry, float> _moveReadyTime = new Dictionary<GreedMoveEntry, float>();
        private readonly List<GreedMoveEntry> _openerCandidates = new List<GreedMoveEntry>();

        // Animator 參數（int）。Idle/Run/Damage/Death 由這一個參數驅動，不用 Trigger：
        // Trigger 在沒有可消耗的轉換時會殘留，之後在錯誤時機觸發，造成動畫打架。
        // 招式與空襲動畫改用 CrossFade 直接播 state（名稱 = 招式 id / Fly / Kick / Dive / Fall），
        // 播放期間把 State 設成 AnimAction（沒有任何 Any State 轉換對應它），避免被 Any State 搶走。
        private static readonly int StateHash = Animator.StringToHash("State");
        private static readonly int MoveSpeedHash = Animator.StringToHash("MoveSpeed");
        private const int AnimIdle = 0, AnimMove = 1, AnimStagger = 3, AnimDead = 4, AnimAction = 99;

        public const string AnimStateFly = "Fly", AnimStateKick = "Kick", AnimStateDive = "Dive", AnimStateFall = "Fall";

        // ════════════════════════════════════════════════════════════
        //  Unity 生命週期
        // ════════════════════════════════════════════════════════════

        private void Awake()
        {
            if (_profile == null)
            {
                Debug.LogError("[Greed] ❌ 沒有指定素材總表（GreedBossProfile），先用預設值執行（沒有招式可出）。", this);
                _profile = ScriptableObject.CreateInstance<GreedBossProfile>();
            }

            Animator = GetComponentInChildren<Animator>(true);
            if (Animator == null)
                Debug.LogError("[Greed] ❌ 找不到 Animator！骨架模型（子物件 Model）必須帶 Animator。");
            else
            {
                // 位移完全由 MoveTowardPlayer() 控制，動畫不可帶動 Boss 本體，否則兩邊會打架。
                Animator.applyRootMotion = false;
                Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // 離開畫面也要跑，否則 AnimEvent 不會觸發、攻擊流程卡住
                Animator.SetFloat(MoveSpeedHash, _runAnimReferenceSpeed > 0f ? _profile.moveSpeed / _runAnimReferenceSpeed : 1f);
                // 素材總表裡換掉的動畫（Clip ≠ 原始 Clip）在這裡套進 Animator，和癡共用同一套
                BossActionFeedback.ApplyAnimationOverrides(Animator, _profile.AllEntries());
            }
            _capsule = GetComponent<CapsuleCollider>();
            _groundY = transform.position.y;
            CurrentHp = MaxHp;
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
            Feedback.Stop();
            EndAirborne();                             // 空襲途中玩家死了：不要懸在空中
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
                { GreedBossStateType.Action,    new GreedActionState(this, _fsm)                             },
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
            HitFeedback.Environment(HitImpactLevel.Heavy, _scaleHitbox.transform.position);   // 踢翻天秤的衝擊
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

            // 受擊音效 / 特效每一下都播；配音有最小間隔，避免連段時每一下都喊（規則同癡）
            if (actualDamage > 0f)
            {
                bool playVoice = Time.time >= _nextHurtVoiceTime;
                if (playVoice) _nextHurtVoiceTime = Time.time + _profile.hurtVoiceMinInterval;
                Feedback.FireAll(_profile.hurt, playVoice);
            }

            // 只在待機/移動時硬直，攻擊與踢天秤中是霸體；加冷卻避免連段把 Boss 鎖死。
            if (_profile.enableStagger
                && Time.time - _lastStaggerTime >= _profile.staggerCooldown
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

            float segment = MaxHp / _hpGateCount;
            float floor = 1f;
            for (int k = 1; k < _hpGateCount; k++)
            {
                float boundary = segment * k;
                if (boundary <= hp) floor = boundary;
            }
            return Mathf.Min(floor, hp);
        }

        /// <summary>Boss 造成的傷害（錢袋重時乘攻擊倍率）。</summary>
        public float ScaleOutgoingDamage(float baseDmg)
        {
            float mult = CurrentPhase == ScalePhase.MoneyBagHeavy ? _bossAttackBoostMultiplier : 1f;
            return baseDmg * mult;
        }

        // ════════════════════════════════════════════════════════════
        //  招式選擇（GreedBossProfile）
        // ════════════════════════════════════════════════════════════

        public bool EvaluateCondition(GreedMoveCondition c)
            => c == null || c.Evaluate(DistanceToPlayer, CurrentPhase, HpRatio);

        private bool IsOpenerReady(GreedMoveEntry m)
            => m != null && m.IsValid && m.canOpen && m.openerWeight > 0f
               && (!_moveReadyTime.TryGetValue(m, out float t) || Time.time >= t)
               && EvaluateCondition(m.openerCondition);

        /// <summary>從起手招裡挑一招（條件成立 + 不在冷卻，照權重隨機）。沒有可出的回傳 null。</summary>
        public GreedMoveEntry PickOpener()
        {
            if (_profile == null) return null;
            _openerCandidates.Clear();
            float total = 0f;
            foreach (var m in _profile.moves)
                if (IsOpenerReady(m)) { _openerCandidates.Add(m); total += m.openerWeight; }
            if (_openerCandidates.Count == 0) return null;

            float r = UnityEngine.Random.value * total;
            foreach (var m in _openerCandidates)
            {
                r -= m.openerWeight;
                if (r <= 0f) return m;
            }
            return _openerCandidates[_openerCandidates.Count - 1];
        }

        /// <summary>目前距離下有沒有任何起手招的「距離條件」成立（不看冷卻 / 相位）：決定要不要繼續追擊。</summary>
        public bool AnyOpenerInRange()
        {
            if (_profile == null) return true;   // 沒有招式表：不追，原地待機
            float d = DistanceToPlayer;
            foreach (var m in _profile.moves)
            {
                if (m == null || !m.IsValid || !m.canOpen) continue;
                var c = m.openerCondition;
                if (d >= c.minDistance && (c.maxDistance <= 0f || d <= c.maxDistance)) return true;
            }
            return false;
        }

        public void MarkMoveUsed(GreedMoveEntry m)
        {
            if (m != null) _moveReadyTime[m] = Time.time + m.Duration + m.cooldown;
        }

        public void SetNextIdleDuration(float seconds) => _nextIdleDuration = seconds;

        public float ConsumeNextIdleDuration()
        {
            float d = _nextIdleDuration >= 0f ? _nextIdleDuration : IdleDuration;
            _nextIdleDuration = -1f;
            return d;
        }

        // ════════════════════════════════════════════════════════════
        //  攻擊判定 / 突進
        // ════════════════════════════════════════════════════════════

        public Vector3 LocalToWorldHitCenter(float forward, float height)
            => transform.position + transform.forward * forward + Vector3.up * height;

        /// <summary>球形判定，打中主角就扣血並回傳 true（玩家翻滾無敵幀由 PlayerController.TakeDamage 自己擋）。</summary>
        public bool TryHitPlayerSphere(Vector3 center, float radius, float baseDamage)
        {
            if (IsDead || BossResultPortal.IsPlayerDefeated) return false;
            foreach (var col in Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!col.CompareTag("Player")) continue;
                var player = col.GetComponentInParent<PlayerController>();
                if (player == null) continue;
                player.TakeDamage(ScaleOutgoingDamage(baseDamage));
                return true;
            }
            return false;
        }

        /// <summary>
        /// 扇形判定（和癡相同）：主角在 radius 內、且在正前方 angle 度的扇形內就扣血。
        /// 招式沒填命中區間時用這個（素材總表的命中時間點 / 距離 / 角度）。
        /// </summary>
        public bool TryHitPlayerFan(float radius, float angle, float baseDamage, string label)
        {
            if (IsDead || BossResultPortal.IsPlayerDefeated || _player == null) return false;
            Vector3 toPlayer = _player.position - transform.position; toPlayer.y = 0f;
            float dist = toPlayer.magnitude;
            float off = toPlayer.sqrMagnitude > 0.0001f ? Vector3.Angle(transform.forward, toPlayer) : 0f;
            bool hit = dist <= radius && (angle >= 360f || off <= angle * 0.5f);

            if (_profile.showHitArea)
            {
                ShowHitFan(radius, angle, hit);
                Debug.Log($"[貪] {label} 扇形判定：{(hit ? "打到" : "沒打到")}　主角距離 {dist:F1}m / 判定距離 {radius:F1}m　偏離正前方 {off:F0}° / 判定角度 ±{angle * 0.5f:F0}°", this);
            }

            if (hit)
            {
                var player = _player.GetComponentInParent<PlayerController>();
                if (player != null) player.TakeDamage(ScaleOutgoingDamage(baseDamage));
            }
            return hit;
        }

        // ── 測試：顯示判定範圍（素材總表勾「顯示判定範圍」時） ──────────

        /// <summary>命中區間結束（或打到）時呼叫：畫出判定球，0.4 秒後消失。紅 = 打到、黃 = 沒打到。</summary>
        public void ReportHitSphere(Vector3 center, float radius, bool hit, string label)
        {
            if (!_profile.showHitArea) return;
            var pts = new List<Vector3>();
            const int seg = 32;
            // 三個互相垂直的圓，看得出是一顆球
            for (int i = 0; i <= seg; i++) { float a = i * Mathf.PI * 2f / seg; pts.Add(center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius); }
            for (int i = 0; i <= seg; i++) { float a = i * Mathf.PI * 2f / seg; pts.Add(center + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius); }
            for (int i = 0; i <= seg; i++) { float a = i * Mathf.PI * 2f / seg; pts.Add(center + new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a)) * radius); }
            SpawnDebugLine("貪_命中判定範圍(測試)", pts, false, hit);
            Debug.Log($"[貪] {label} 球形判定：{(hit ? "打到" : "沒打到")}　半徑 {radius:F1}m", this);
        }

        private void ShowHitFan(float radius, float angle, bool hit)
        {
            const int seg = 32;
            float half = Mathf.Min(angle, 360f) * 0.5f;
            Vector3 origin = transform.position + Vector3.up * 0.05f;
            var pts = new List<Vector3>();
            if (angle < 360f) pts.Add(origin);
            for (int i = 0; i <= seg; i++)
                pts.Add(origin + Quaternion.Euler(0f, -half + half * 2f * i / seg, 0f) * transform.forward * radius);
            SpawnDebugLine("貪_命中判定範圍(測試)", pts, angle < 360f, hit);
        }

        private static void SpawnDebugLine(string name, List<Vector3> pts, bool loop, bool hit)
        {
            var go = new GameObject(name);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = loop;
            lr.widthMultiplier = 0.08f;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = lr.endColor = hit ? new Color(1f, 0.15f, 0.15f) : new Color(1f, 0.85f, 0.1f);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.positionCount = pts.Count;
            lr.SetPositions(pts.ToArray());
            Destroy(lr.material, 0.4f);
            Destroy(go, 0.4f);
        }

        /// <summary>招式突進：往面向方向移動，會被障礙物與主角擋住。</summary>
        public void LungeStep(float distance)
        {
            if (distance <= 0f) return;
            Vector3 dir = transform.forward; dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();
            float step = ClampStepToAvoidObstacles(dir, distance, out _);
            step = ClampStepToAvoidPlayer(step);
            transform.position += dir * step;
            DepenetrateFromObstacles();
        }

        // ════════════════════════════════════════════════════════════
        //  空襲（GreedKickScaleState 用）
        // ════════════════════════════════════════════════════════════

        public void BeginAirborne() => _airborne = true;

        /// <summary>空中移動：不做障礙物檢查（會飛越天秤），落地時再推出重疊。</summary>
        public void SetAirPosition(Vector3 pos) => transform.position = pos;

        /// <summary>空中姿態（含轉彎傾斜）。落地時 Land() 會把傾斜歸零。</summary>
        public void SetAirRotation(Quaternion rot) => transform.rotation = rot;

        /// <summary>盤旋中心（天秤位置；沒設定天秤時用 Boss 自己）。</summary>
        public Vector3 ScaleCenter => _scale != null ? _scale.transform.position : transform.position;

        /// <summary>落地：貼回地面並推出重疊（例如砸在天秤旁）。</summary>
        public void Land()
        {
            Vector3 p = transform.position; p.y = _groundY;

            // 主角在俯衝途中跑到落點上：把 Boss 往外推，不要壓在主角身上
            if (_player != null)
            {
                Vector3 away = p - _player.position; away.y = 0f;
                if (away.magnitude < MinDistanceToPlayer)
                {
                    if (away.sqrMagnitude < 0.0001f) away = -transform.forward;
                    p = new Vector3(_player.position.x, _groundY, _player.position.z) + away.normalized * MinDistanceToPlayer;
                }
            }

            transform.position = p;
            Vector3 fwd = transform.forward; fwd.y = 0f;   // 去掉空中的傾斜 / 俯仰，只留水平朝向
            if (fwd.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(fwd.normalized);
            _airborne = false;
            DepenetrateFromObstacles();
        }

        /// <summary>還在空中就落地（被強制中斷時呼叫，重複呼叫無害）。</summary>
        public void EndAirborne()
        {
            if (Animator != null) Animator.speed = 1f;
            if (_airborne) Land();
        }

        /// <summary>天秤一側的定位點（世界座標）與面向天秤的方向。</summary>
        public void GetKickPoint(out Vector3 point, out Vector3 faceDir)
        {
            var cfg = _profile.aerialKick;
            Transform s = _scale != null ? _scale.transform : transform;
            Vector3 local = cfg.kickPointLocal;
            Vector3 a = KickCandidate(s, new Vector3(Mathf.Abs(local.x), 0f, local.z));
            Vector3 b = KickCandidate(s, new Vector3(-Mathf.Abs(local.x), 0f, local.z));

            Vector3 playerPos = _player != null ? _player.position : transform.position;
            bool pickA = cfg.kickSide switch
            {
                GreedKickSide.離Boss近的那側 => FlatDist(a, transform.position) <= FlatDist(b, transform.position),
                GreedKickSide.離主角遠的那側 => FlatDist(a, playerPos) >= FlatDist(b, playerPos),
                GreedKickSide.離主角近的那側 => FlatDist(a, playerPos) <= FlatDist(b, playerPos),
                _ => UnityEngine.Random.value < 0.5f,
            };
            point = pickA ? a : b;
            point.y = _groundY + local.y;

            faceDir = s.position - point; faceDir.y = 0f;
            if (faceDir.sqrMagnitude < 0.0001f) faceDir = transform.forward;
            faceDir.Normalize();
        }

        private static Vector3 KickCandidate(Transform scale, Vector3 localFlat)
            => scale.position + scale.rotation * localFlat;   // 用旋轉不用 TransformPoint：天秤的縮放不影響距離

        private static float FlatDist(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        /// <summary>俯衝落點：主角腳下（地面高度），可依主角移動方向預判最多 maxLead 公尺。</summary>
        public Vector3 GetDiveTarget(float maxLead)
        {
            Vector3 t = _player != null ? _player.position : transform.position;
            if (maxLead > 0f && _player != null)
            {
                var rb = _player.GetComponentInParent<Rigidbody>();
                if (rb != null)
                {
                    Vector3 v = rb.linearVelocity; v.y = 0f;
                    t += Vector3.ClampMagnitude(v * 0.5f, maxLead);
                }
            }
            t.y = _groundY;

            // 停在主角前方（兩者碰撞體不重疊），範圍傷害照樣打得到
            Vector3 approach = t - transform.position; approach.y = 0f;
            if (approach.sqrMagnitude > MinDistanceToPlayer * MinDistanceToPlayer)
                t -= approach.normalized * MinDistanceToPlayer;
            return t;
        }

        /// <summary>落地衝擊範圍傷害。</summary>
        public void ImpactAoE(float radius, float damage)
        {
            if (radius <= 0f || damage <= 0f) return;
            TryHitPlayerSphere(transform.position + Vector3.up * 0.5f, radius, damage);
        }

        public void FaceDirection(Vector3 dir, float dt, float turnSpeed = 10f)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir.normalized), turnSpeed * dt);
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
        /// 依 FSM 狀態切換 Idle / Run / Damage / Death 動畫。
        /// 招式（Action）與空襲（KickScale）由狀態自己呼叫 PlayAnimState 播指定 state；
        /// KickScale 沒設定空襲動畫時（舊流程）維持 Idle。
        /// </summary>
        public void SetAnimState(GreedBossStateType type)
        {
            if (Animator == null || Animator.runtimeAnimatorController == null) return;
            int value = type switch
            {
                GreedBossStateType.Move    => AnimMove,
                GreedBossStateType.Stagger => AnimStagger,
                GreedBossStateType.Dead    => AnimDead,
                _                          => AnimIdle,
            };
            if (type == GreedBossStateType.Move) _blockedTime = 0f;
            Animator.speed = 1f;
            Animator.SetInteger(StateHash, value);
        }

        /// <summary>直接播 Animator 裡名為 stateName 的 state（招式 / 空襲用），從頭開始。</summary>
        public void PlayAnimState(string stateName, float speed)
        {
            if (Animator == null || Animator.runtimeAnimatorController == null) return;
            int hash = Animator.StringToHash(stateName);
            if (!Animator.HasState(0, hash))
            {
                Debug.LogWarning($"[Greed] Animator 沒有 state「{stateName}」，請在 GreedBossProfile 按「同步到 Animator」。");
                return;
            }
            Animator.SetInteger(StateHash, AnimAction);
            Animator.speed = speed;
            float fade = _profile != null ? _profile.crossFade : 0.1f;
            Animator.CrossFadeInFixedTime(hash, fade, 0, 0f);
        }

        /// <summary>把正在播的 state 直接拉到指定的動畫比例（停格中的循環區段用，需搭配 Animator.speed = 0）。</summary>
        public void ScrubAnimState(string stateName, float normalizedTime)
        {
            if (Animator == null || Animator.runtimeAnimatorController == null) return;
            Animator.Play(Animator.StringToHash(stateName), 0, normalizedTime);
        }

        /// <summary>停格（0）或恢復播放速度。</summary>
        public void SetAnimSpeed(float speed)
        {
            if (Animator != null) Animator.speed = speed;
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

            float step = _profile.moveSpeed * dt;
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

            UpdateMoveAnim(step > _profile.moveSpeed * dt * 0.1f, dt);
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
            Feedback.Stop();
            try { _spawner?.ClearAll(); }
            catch (System.Exception e) { Debug.LogError($"[Greed] 清除錢袋失敗，仍繼續通關流程：{e}"); }
            if (BossResultPortal.Instance == null)
                Debug.LogError("[Greed] 場景裡找不到 BossResultPortal，通關流程無法啟動！");
            BossResultPortal.Instance?.OnBossDefeated();
        }

        // ════════════════════════════════════════════════════════════
        //  Animation Events
        // ════════════════════════════════════════════════════════════

        public void AnimEvent_KickScaleEnd() => OnKickScaleAnimEnd?.Invoke();

        // 舊的 Attack1 動畫事件。命中與結束時間已改由 GreedBossProfile 的命中區間 / 動畫長度決定，
        // 這兩個留著只是讓動畫裡既有的事件有接收者（不然 Unity 會報錯）。
        // 傷害判定不在這裡做（交給命中區間）；只保留打擊感：砸下去那一幀觸發環境晃動
        // （只晃鏡頭、不震手把，離越遠越小）。遲到的事件（已離開出招／已死亡）忽略。
        public void AnimEvent_AttackHit()
        {
            if (IsDead || BossResultPortal.IsPlayerDefeated) return;
            if (CurrentStateType != GreedBossStateType.Action) return;
            HitFeedback.Environment(HitImpactLevel.Heavy, transform.position);
        }
        public void AnimEvent_AttackEnd() { }

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
            if (_profile == null) return;

            // 招式命中球（目前 Boss 位置 / 朝向）
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.6f);
            foreach (var m in _profile.moves)
            {
                if (m == null) continue;
                foreach (var w in m.hitWindows)
                    Gizmos.DrawWireSphere(LocalToWorldHitCenter(w.forward, w.height), w.radius);

                if (m.UsesFanHit)   // 單次扇形判定（和癡相同的畫法）
                {
                    float half = Mathf.Min(m.hitAngle, 360f) * 0.5f;
                    Vector3 prevPt = transform.position + Quaternion.Euler(0f, -half, 0f) * transform.forward * m.hitRadius;
                    for (int s = 1; s <= 24; s++)
                    {
                        Vector3 next = transform.position + Quaternion.Euler(0f, -half + half * 2f * s / 24f, 0f) * transform.forward * m.hitRadius;
                        Gizmos.DrawLine(prevPt, next);
                        prevPt = next;
                    }
                    if (half < 180f)
                    {
                        Gizmos.DrawLine(transform.position, transform.position + Quaternion.Euler(0f, -half, 0f) * transform.forward * m.hitRadius);
                        Gizmos.DrawLine(transform.position, transform.position + Quaternion.Euler(0f, half, 0f) * transform.forward * m.hitRadius);
                    }
                }
            }

            // 空襲：天秤兩側定位點
            if (_scale != null)
            {
                var cfg = _profile.aerialKick;
                float gy = Application.isPlaying ? _groundY : transform.position.y;
                Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);
                foreach (float sx in new[] { 1f, -1f })
                {
                    Vector3 p = KickCandidate(_scale.transform, new Vector3(sx * Mathf.Abs(cfg.kickPointLocal.x), 0f, cfg.kickPointLocal.z));
                    p.y = gy + cfg.kickPointLocal.y;
                    Gizmos.DrawWireSphere(p, 0.5f);
                    Gizmos.DrawLine(p, new Vector3(p.x, gy, p.z));
                }

                // 盤旋圈
                Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.7f);
                Vector3 c = _scale.transform.position; c.y = gy + cfg.cruiseHeight;
                Vector3 prev = c + new Vector3(cfg.orbitRadius, 0f, 0f);
                for (int i = 1; i <= 64; i++)
                {
                    float a = i / 64f * Mathf.PI * 2f;
                    Vector3 q = c + new Vector3(Mathf.Cos(a) * cfg.orbitRadius, 0f, Mathf.Sin(a) * cfg.orbitRadius);
                    Gizmos.DrawLine(prev, q);
                    prev = q;
                }
            }
        }
    }
}