using System;
using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// Boss「癡」主控制器。
    /// 場地機制（詳見「癡關卡規則」）：
    ///   善/惡區(5s) → 緩衝 → 善/惡區(5s) → 緩衝 → 打鬥窗口(10s) → 嘶吼(1s) → 地面擴散變黑(2s) → 重複
    /// 惡區每秒傷害依癡的血量分三段；安全區 1~3 個（假隨機）、位置每輪隨機、不貼邊，
    /// 並隨癡受到的傷害線性向外擴張。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class FoolishBossController : MonoBehaviour, IDamageable, IBossHealth
    {
        // ── References ───────────────────────────────────────────────
        [Header("References")]
        [SerializeField] private Transform _player;

        /// <summary>惡區視覺物件。</summary>
        [SerializeField] private EvilZone _evilZone;

        /// <summary>善區物件池（數量決定一輪最多能出現幾個）。</summary>
        [SerializeField] private SafeZone[] _safeZones = new SafeZone[4];

        public Animator Animator { get; private set; }

        /// <summary>配音（選配）。沒掛 FoolishVoice 元件就是 null，呼叫端用 ?. 即可。</summary>
        public FoolishVoice Voice { get; private set; }

        // ── Boss 基本數值 ─────────────────────────────────────────────
        [Header("Boss Stats")]
        [SerializeField] private string _displayName = "心魔-癡";
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

        [Tooltip("沒有動畫事件（還沒綁 Animator Controller）時，攻擊狀態最多持續幾秒就結束，避免卡死。")]
        [SerializeField] private float _attackFallbackDuration = 1.5f;

        // ── 模型尺寸 ─────────────────────────────────────────────────
        [Header("模型尺寸")]
        [Tooltip("勾選：開始時自動量模型實際高度，縮放到下面的目標高度並讓腳貼地。預設不勾，維持模型原始尺寸。")]
        [SerializeField] private bool _autoFitModel = false;
        [Tooltip("目標身高（公尺）。規則文件：癡體型 300cm。只有勾選 Auto Fit Model 才會用到。")]
        [SerializeField] private float _modelTargetHeight = 3f;
        [Tooltip("勾選：開始時依模型實際大小調整碰撞膠囊（高度、半徑、中心），不改變模型本身大小。")]
        [SerializeField] private bool _autoFitCollider = true;

        // ── 場地機制數值 ──────────────────────────────────────────────
        [Header("場地機制")]
        [SerializeField] private FoolishArenaSettings _arena = new FoolishArenaSettings();

        // ── Stagger（選配） ───────────────────────────────────────────
        [Header("Stagger (Optional)")]
        [SerializeField] public bool enableStagger = false;
        [SerializeField] public float StaggerDuration = 0.5f;

        // ── 公開屬性 ─────────────────────────────────────────────────
        public float CurrentHp { get; private set; }
        public float MaxHp => _maxHp;
        public bool IsDead => CurrentHp <= 0f;
        public string DisplayName => _displayName;
        public float IdleDuration => _idleDuration;
        public float AttackFallbackDuration => _attackFallbackDuration;
        public float Attack1Range => _attack1Range;
        public float Attack2Range => _attack2Range;
        public float Attack3Range => _attack3Range;
        public float HpRatio => _maxHp > 0f ? CurrentHp / _maxHp : 0f;
        public FoolishBossStateType CurrentStateType => _fsm?.CurrentType ?? FoolishBossStateType.Idle;
        public float DistanceToPlayer => _player != null
                                         ? Vector3.Distance(transform.position, _player.position)
                                         : float.MaxValue;

        /// <summary>場地循環的細部階段。</summary>
        public FoolishCyclePhase CyclePhase { get; private set; } = FoolishCyclePhase.ZoneShow;

        /// <summary>粗分相位：只有打鬥窗口是 Battle，其餘都是 Evade。</summary>
        public FoolishPhase CurrentPhase => CyclePhase == FoolishCyclePhase.Battle ? FoolishPhase.Battle : FoolishPhase.Evade;

        /// <summary>目前階段剩餘秒數。</summary>
        public float PhaseTimeRemaining => Mathf.Max(0f, _phaseTimer);

        /// <summary>目前場上顯示的善區數量（預告或生效都算；沒有善區的階段為 0）。</summary>
        public int ActiveSafeZoneCount { get; private set; }

        /// <summary>目前場上的善區是預告（淡色細圈、還不提供保護），而不是生效。</summary>
        public bool SafeZonesArePreview { get; private set; }

        /// <summary>目前惡區是否生效（會扣血）。</summary>
        public bool EvilZoneDamaging => !IsDead && CyclePhase == FoolishCyclePhase.ZoneShow;

        /// <summary>目前惡區每秒傷害（依癡血量分三段）。</summary>
        public float CurrentEvilDps => _arena.GetEvilDps(HpRatio);

        /// <summary>玩家目前是否站在善區內。</summary>
        public bool PlayerInSafeZone { get; private set; }

        // ── 動畫事件 ─────────────────────────────────────────────────
        public event Action OnAttackAnimEnd;

        // ── 內部計時 ─────────────────────────────────────────────────
        private FoolishBossStateMachine _fsm;
        private PlayerController _playerController;
        private float _phaseTimer;
        private float _phaseDuration;
        private int _zoneRound;           // 目前是第幾輪善惡區（0 起算）
        private bool _earlyEndUsed;       // 打鬥窗口提前結束只觸發一次
        private int _lastSafeCount;       // 上一輪安全區數量（假隨機用）
        private int _sameCountStreak;     // 連續幾輪相同
        private readonly List<SafeZone> _shownZones = new List<SafeZone>();   // 已生效（提供保護）的善區

        // 下一輪善區的預先安排：先以預告外觀顯示，惡區生效時沿用同一批位置
        private struct ZonePlan
        {
            public SafeZone zone;
            public Vector2 center;
            public float radius;
        }
        private readonly List<ZonePlan> _plan = new List<ZonePlan>();

        // ════════════════════════════════════════════════════════════
        //  Unity 生命周期
        // ════════════════════════════════════════════════════════════

        private void Awake()
        {
            Animator = GetComponent<Animator>();
            // 配音元件放在癡本身、子物件、或場景裡任何一個物件上都可以
            Voice = GetComponent<FoolishVoice>();
            if (Voice == null) Voice = GetComponentInChildren<FoolishVoice>();
            if (Voice == null) Voice = FindFirstObjectByType<FoolishVoice>();
            CurrentHp = _maxHp;
            if (_player != null) _playerController = _player.GetComponent<PlayerController>();

            // 癡的位置由程式直接控制（MoveTowardPlayer 改 transform），不能讓物理引擎推它：
            // 設成 Kinematic、不受重力。碰撞體仍在，所以主角撞上去是被擋住，不會把癡推走。
            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            FitModelToHeight();
            BuildFSM();
        }

        /// <summary>
        /// 依模型實際渲染範圍縮放到目標身高、腳貼地，並調整碰撞膠囊。重複呼叫不會累積（量到的高度已是目標時倍率為 1）。
        /// </summary>
        [ContextMenu("套用模型自動縮放與碰撞調整")]
        private void FitModelToHeight()
        {
            if (!_autoFitModel && !_autoFitCollider) return;

            Transform modelRoot = null;
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child.GetComponentInChildren<Renderer>() != null) { modelRoot = child; break; }
            }
            if (modelRoot == null) return;

            if (!TryGetBounds(modelRoot, out Bounds b) || b.size.y < 0.01f) return;

            float originalHeight = b.size.y;
            if (_autoFitModel)
            {
                float factor = _modelTargetHeight / originalHeight;
                modelRoot.localScale *= factor;

                // 縮放後重新量一次，讓腳底貼在癡的根物件高度
                TryGetBounds(modelRoot, out b);
                modelRoot.position += Vector3.up * (transform.position.y - b.min.y);
                TryGetBounds(modelRoot, out b);
            }

            var capsule = GetComponent<CapsuleCollider>();
            if (_autoFitCollider && capsule != null)
            {
                // 碰撞膠囊貼合模型實際範圍：中心在模型範圍中心，高度 = 模型高度，半徑取較窄那一邊的 35%
                Vector3 localCenter = transform.InverseTransformPoint(b.center);
                capsule.direction = 1;
                capsule.height = b.size.y;
                capsule.center = localCenter;
                capsule.radius = Mathf.Clamp(Mathf.Min(b.size.x, b.size.z) * 0.35f, 0.3f, b.size.y * 0.5f);
            }

            Debug.Log($"[FoolishBoss] 模型實際大小：高 {b.size.y:F3}m / 寬 {b.size.x:F3}m / 深 {b.size.z:F3}m" +
                      (_autoFitModel ? $"（已縮放到 {_modelTargetHeight:F2}m）" : "（維持原始尺寸）") +
                      (_autoFitCollider ? "，碰撞膠囊已貼合模型" : ""));
        }

        /// <summary>
        /// 用網格本身的範圍乘上 Transform 矩陣算世界範圍。不用 Renderer.bounds：它在 Awake 時可能還沒更新，
        /// 會量出錯誤的大小（實測量到過 0.03m）。
        /// </summary>
        private static bool TryGetBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            bool found = false;

            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null)
                    EncapsulateMesh(ref bounds, ref found, mf.sharedMesh.bounds, mf.transform.localToWorldMatrix);

            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (smr.sharedMesh != null)
                    EncapsulateMesh(ref bounds, ref found, smr.sharedMesh.bounds, smr.transform.localToWorldMatrix);

            return found;
        }

        private static void EncapsulateMesh(ref Bounds bounds, ref bool found, Bounds local, Matrix4x4 toWorld)
        {
            Vector3 c = local.center, e = local.extents;
            for (int i = 0; i < 8; i++)
            {
                var corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x,
                                             (i & 2) == 0 ? -e.y : e.y,
                                             (i & 4) == 0 ? -e.z : e.z);
                Vector3 p = toWorld.MultiplyPoint3x4(corner);
                if (!found) { bounds = new Bounds(p, Vector3.zero); found = true; }
                else bounds.Encapsulate(p);
            }
        }

        private void Start()
        {
            if (_player == null)
            {
                _playerController = FindFirstObjectByType<PlayerController>();
                if (_playerController != null) _player = _playerController.transform;
            }

            // 惡區要蓋住整個方形場地（多留 0.5m 避免邊緣露出）
            if (_evilZone != null) _evilZone.Initialize(_arena.arenaCenter, _arena.arenaHalfSize + 0.5f);

            // 開場先進一段緩衝（_zoneRound = -1），讓玩家在第一輪善惡區出現前就看到善區預告
            HideAllSafeZones();
            _zoneRound = -1;
            EnterBuffer();
        }

        // 玩家已經死亡（失敗流程進行中）就停手，不要再追擊/攻擊倒下的玩家。
        private void Update()
        {
            // _fsm 是純 C# 物件、沒有序列化：Play 中途重新編譯（Domain Reload 關閉時不會重跑 Awake）會變成 null，直接略過避免洗紅字
            if (_fsm == null || BossResultPortal.IsPlayerDefeated) return;
            UpdateCycle(Time.deltaTime);
            _fsm.Update(Time.deltaTime);
        }

        private void FixedUpdate()
        {
            if (_fsm == null || BossResultPortal.IsPlayerDefeated) return;
            _fsm.FixedUpdate(Time.fixedDeltaTime);
        }

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
        //  場地循環
        // ════════════════════════════════════════════════════════════

        private void UpdateCycle(float dt)
        {
            if (IsDead) return;

            _phaseTimer -= dt;

            // 惡區只在「善/惡區出現」階段生效
            if (CyclePhase == FoolishCyclePhase.ZoneShow) ApplyEvilDamage(dt);
            else PlayerInSafeZone = false;

            // 擴散：視覺進度
            if (CyclePhase == FoolishCyclePhase.Spread && _evilZone != null && _phaseDuration > 0f)
                _evilZone.Show(1f - Mathf.Clamp01(_phaseTimer / _phaseDuration));

            if (_phaseTimer <= 0f) AdvanceCycle();
        }

        private void AdvanceCycle()
        {
            switch (CyclePhase)
            {
                case FoolishCyclePhase.ZoneShow:
                    EnterBuffer();
                    break;
                case FoolishCyclePhase.Buffer:
                    if (_zoneRound + 1 < _arena.zoneRounds)
                    {
                        _zoneRound++;
                        EnterZoneShow();
                    }
                    else EnterBattle();
                    break;
                case FoolishCyclePhase.Battle:
                    EnterRoar();
                    break;
                case FoolishCyclePhase.Roar:
                    EnterSpread();
                    break;
                case FoolishCyclePhase.Spread:
                    _zoneRound = 0;
                    EnterZoneShow();
                    break;
            }
        }

        private void SetPhase(FoolishCyclePhase phase, float duration)
        {
            CyclePhase = phase;
            _phaseDuration = duration;
            _phaseTimer = duration;
        }

        private void EnterZoneShow()
        {
            SetPhase(FoolishCyclePhase.ZoneShow, _arena.zoneShowDuration);
            if (_evilZone != null) _evilZone.Show(1f);
            ActivatePlannedZones(); // 預告的那批善區現在正式生效
        }

        private void EnterBuffer()
        {
            SetPhase(FoolishCyclePhase.Buffer, _arena.bufferDuration);
            HideAllSafeZones();
            if (_evilZone != null) _evilZone.Hide();

            // 緩衝之後還有下一輪善惡區：現在就預告它的位置；之後是打鬥窗口就不預告
            if (_zoneRound + 1 < _arena.zoneRounds) PlanNextRound();
        }

        private void EnterBattle()
        {
            SetPhase(FoolishCyclePhase.Battle, _arena.battleDuration);
            HideAllSafeZones();
            if (_evilZone != null) _evilZone.Hide();
        }

        private void EnterRoar()
        {
            SetPhase(FoolishCyclePhase.Roar, _arena.roarDuration);
            HideAllSafeZones();
            if (_evilZone != null) _evilZone.Hide();

            // 下一個大循環第一輪善區的預告：嘶吼和擴散期間都看得到
            PlanNextRound();

            Voice?.Play(FoolishVoiceId.Roar);
            if (Animator != null && Animator.runtimeAnimatorController != null) Animator.SetTrigger("Roar");
        }

        private void EnterSpread()
        {
            SetPhase(FoolishCyclePhase.Spread, _arena.spreadDuration);
            if (_evilZone != null) _evilZone.Show(0f);
        }

        /// <summary>測試用：直接跳到下一個階段。</summary>
        public void DebugSkipPhase() => AdvanceCycle();

        // ════════════════════════════════════════════════════════════
        //  惡區傷害
        // ════════════════════════════════════════════════════════════

        private void ApplyEvilDamage(float dt)
        {
            if (_playerController == null || _player == null) return;

            PlayerInSafeZone = IsInAnySafeZone(_player.position);
            if (PlayerInSafeZone) return;

            _playerController.TakeEnvironmentDamage(CurrentEvilDps * dt);
        }

        private bool IsInAnySafeZone(Vector3 worldPoint)
        {
            foreach (var z in _shownZones)
                if (z != null && z.Contains(worldPoint)) return true;
            return false;
        }

        // ════════════════════════════════════════════════════════════
        //  安全區：數量（假隨機）、位置（隨機、不貼邊、不重疊）、大小（隨癡受傷擴張）
        // ════════════════════════════════════════════════════════════

        private void HideAllSafeZones()
        {
            _shownZones.Clear();
            _plan.Clear();
            ActiveSafeZoneCount = 0;
            SafeZonesArePreview = false;
            PlayerInSafeZone = false;
            foreach (var z in _safeZones)
                if (z != null) z.Hide();
        }

        /// <summary>
        /// 抽出下一輪善區的數量、位置、大小，並先以「預告」外觀顯示（淡色細圈、還不提供保護）。
        /// 惡區生效時由 ActivatePlannedZones 沿用同一批，不會重抽。
        /// </summary>
        private void PlanNextRound()
        {
            HideAllSafeZones();

            var pool = new List<SafeZone>();
            foreach (var z in _safeZones) if (z != null) pool.Add(z);
            if (pool.Count == 0) return;

            int count = Mathf.Min(RollSafeZoneCount(), pool.Count);

            var centers = new List<Vector2>();
            var radii = new List<float>();
            for (int i = 0; i < count; i++)
            {
                var zone = pool[i];
                float radius = GetSafeZoneRadius(zone);
                Vector2 pos = PickSafeZonePosition(radius, centers, radii);
                centers.Add(pos);
                radii.Add(radius);

                _plan.Add(new ZonePlan { zone = zone, center = pos, radius = radius });
                zone.Show(new Vector3(pos.x, 0f, pos.y), radius, preview: true);
            }

            ActiveSafeZoneCount = _plan.Count;
            SafeZonesArePreview = true;
        }

        /// <summary>把預告的善區變成生效（實心圓盤、提供保護）。沒有預先安排就當場抽一次，避免出現沒有善區的惡區。</summary>
        private void ActivatePlannedZones()
        {
            if (_plan.Count == 0) PlanNextRound();

            _shownZones.Clear();
            foreach (var p in _plan)
            {
                p.zone.Show(new Vector3(p.center.x, 0f, p.center.y), p.radius, preview: false);
                _shownZones.Add(p.zone);
            }

            ActiveSafeZoneCount = _shownZones.Count;
            SafeZonesArePreview = false;
        }

        /// <summary>安全區半徑 = 基本半徑 + 額外半徑 × 癡已損失血量比例（線性向外擴張）。</summary>
        private float GetSafeZoneRadius(SafeZone zone)
        {
            float baseRadius = _arena.safeZoneBaseRadius > 0f ? _arena.safeZoneBaseRadius : zone.GetSceneRadius();
            return baseRadius + _arena.safeZoneExtraRadiusAtZeroHp * (1f - HpRatio);
        }

        /// <summary>
        /// 假隨機：數量在 min~max 之間均勻抽，但數量是最少或最多時，若已連續 maxSameCountStreak 輪相同就排除，
        /// 避免連續好幾輪都是 1 個或 3 個。
        /// </summary>
        private int RollSafeZoneCount()
        {
            int min = Mathf.Max(1, _arena.safeZoneMin);
            int max = Mathf.Max(min, _arena.safeZoneMax);

            var candidates = new List<int>();
            for (int n = min; n <= max; n++)
            {
                bool isExtreme = n == min || n == max;
                bool blocked = isExtreme && n == _lastSafeCount && _sameCountStreak >= _arena.maxSameCountStreak;
                if (!blocked) candidates.Add(n);
            }
            if (candidates.Count == 0) candidates.Add(min);

            int pick = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            _sameCountStreak = pick == _lastSafeCount ? _sameCountStreak + 1 : 1;
            _lastSafeCount = pick;
            return pick;
        }

        /// <summary>
        /// 在方形場地內隨機找位置：整個圓離邊緣至少 edgeMargin，且與已選的圓至少隔 safeZoneGap。
        /// 試 30 次找不到就用最後一次的位置（場地太小時寧可重疊也不要卡住）。
        /// </summary>
        private Vector2 PickSafeZonePosition(float radius, List<Vector2> centers, List<float> radii)
        {
            float range = Mathf.Max(0f, _arena.arenaHalfSize - _arena.edgeMargin - radius);
            Vector2 candidate = _arena.arenaCenter;

            for (int attempt = 0; attempt < 30; attempt++)
            {
                candidate = _arena.arenaCenter + new Vector2(
                    UnityEngine.Random.Range(-range, range),
                    UnityEngine.Random.Range(-range, range));

                bool ok = true;
                for (int i = 0; i < centers.Count; i++)
                {
                    if (Vector2.Distance(candidate, centers[i]) < radius + radii[i] + _arena.safeZoneGap)
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok) return candidate;
            }
            return candidate;
        }

        // ════════════════════════════════════════════════════════════
        //  戰鬥介面
        // ════════════════════════════════════════════════════════════

        public void TakeDamage(float amount)
        {
            if (IsDead) return;

            // 只有打鬥窗口能造成傷害（可在 Inspector 勾選 vulnerableOutsideBattle 改成隨時可打）
            if (CyclePhase != FoolishCyclePhase.Battle && !_arena.vulnerableOutsideBattle)
                return;

            float ratioBefore = HpRatio;
            CurrentHp = Mathf.Max(0f, CurrentHp - amount);

            if (CurrentHp <= 0f)
            {
                _fsm.Force(FoolishBossStateType.Dead);
                return;
            }

            Voice?.Play(FoolishVoiceId.Hurt);

            // 打鬥窗口中血量降到門檻以下 → 窗口提前結束（每場戰鬥只觸發一次）
            if (CyclePhase == FoolishCyclePhase.Battle && !_earlyEndUsed
                && ratioBefore > _arena.battleEarlyEndHpRatio && HpRatio <= _arena.battleEarlyEndHpRatio)
            {
                _earlyEndUsed = true;
                EnterRoar();
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
            HideAllSafeZones();
            if (_evilZone) _evilZone.Hide();
            BossResultPortal.Instance?.OnBossDefeated();
        }

        // ════════════════════════════════════════════════════════════
        //  Animation Events
        // ════════════════════════════════════════════════════════════

        public void AnimEvent_AttackEnd() => OnAttackAnimEnd?.Invoke();
    }
}
