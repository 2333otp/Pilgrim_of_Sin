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
    /// 數值與素材（動畫、音效、配音、特效、攻擊判定、場地機制）全部在 FoolishBossProfile 裡設定。
    /// </summary>
    public class FoolishBossController : MonoBehaviour, IDamageable, IBossHealth, IInterruptible
    {
        // ── References ───────────────────────────────────────────────
        [Header("素材總表（數值、動畫、音效、配音、特效、場地機制都在這裡改）")]
        [SerializeField] private FoolishBossProfile _profile;

        [Header("References")]
        [SerializeField] private Transform _player;

        /// <summary>惡區視覺物件。</summary>
        [SerializeField] private EvilZone _evilZone;

        /// <summary>善區物件池（數量決定一輪最多能出現幾個）。</summary>
        [SerializeField] private SafeZone[] _safeZones = new SafeZone[4];

        /// <summary>癡模型上的 Animator（在模型子物件上，骨架在那裡）。</summary>
        public Animator Animator { get; private set; }

        /// <summary>素材播放器：音效、配音、特效、攻擊命中時間點。</summary>
        public BossActionFeedback Feedback { get; private set; }

        public FoolishBossProfile Profile => _profile;
        private FoolishArenaSettings Arena => _profile.arena;

        // ── 公開屬性 ─────────────────────────────────────────────────
        public float CurrentHp { get; private set; }
        public float MaxHp => _profile.maxHp;
        public bool IsDead => CurrentHp <= 0f;
        public string DisplayName => _profile.displayName;
        public float IdleDuration => _profile.idleDuration;
        public float Attack1Range => _profile.attack1.attackRange;
        public float Attack2Range => _profile.attack2.attackRange;
        public float Attack3Range => _profile.attack3.attackRange;
        public bool EnableStagger => _profile.enableStagger;
        public float HpRatio => MaxHp > 0f ? CurrentHp / MaxHp : 0f;
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
        public float CurrentEvilDps => Arena.GetEvilDps(HpRatio);

        /// <summary>玩家目前是否站在善區內。</summary>
        public bool PlayerInSafeZone { get; private set; }

        // ── 內部計時 ─────────────────────────────────────────────────
        private FoolishBossStateMachine _fsm;
        private PlayerController _playerController;
        private float _nextHurtVoiceTime;
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
            if (_profile == null)
            {
                Debug.LogWarning("[FoolishBoss] 沒有指定素材總表（FoolishBossProfile），先用預設值執行。", this);
                _profile = ScriptableObject.CreateInstance<FoolishBossProfile>();
            }

            // Animator 在模型子物件上（骨架在那裡，動畫路徑才對得上）；優先找有指定 Controller 的那個
            Animator = null;
            foreach (var a in GetComponentsInChildren<Animator>(true))
                if (Animator == null || (Animator.runtimeAnimatorController == null && a.runtimeAnimatorController != null))
                    Animator = a;

            Feedback = GetComponent<BossActionFeedback>();
            if (Feedback == null) Feedback = gameObject.AddComponent<BossActionFeedback>();
            Feedback.Initialize(Animator, _profile.AllEntries(), _profile.mixerGroup);

            // 移動動畫播放倍率（Animator 的 Move state 速度綁 MoveSpeed 參數），腳步跟上實際移動速度，不會在地上滑
            if (Animator != null && Animator.runtimeAnimatorController != null)
                foreach (var p in Animator.parameters)
                    if (p.name == "MoveSpeed" && p.type == AnimatorControllerParameterType.Float)
                        Animator.SetFloat("MoveSpeed", _profile.MoveAnimSpeed);

            CurrentHp = MaxHp;
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
            if (_profile == null) return;
            bool autoFitModel = _profile.autoFitModel;
            bool autoFitCollider = _profile.autoFitCollider;
            float targetHeight = _profile.modelTargetHeight;
            if (!autoFitModel && !autoFitCollider) return;

            Transform modelRoot = null;
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child.GetComponentInChildren<Renderer>() != null) { modelRoot = child; break; }
            }
            if (modelRoot == null) return;

            if (!TryGetBounds(modelRoot, out Bounds b) || b.size.y < 0.01f) return;

            float originalHeight = b.size.y;
            if (autoFitModel)
            {
                float factor = targetHeight / originalHeight;
                modelRoot.localScale *= factor;

                // 縮放後重新量一次，讓腳底貼在癡的根物件高度
                TryGetBounds(modelRoot, out b);
                modelRoot.position += Vector3.up * (transform.position.y - b.min.y);
                TryGetBounds(modelRoot, out b);
            }

            var capsule = GetComponent<CapsuleCollider>();
            if (autoFitCollider && capsule != null)
            {
                // 碰撞膠囊貼合模型實際範圍：中心在模型範圍中心，高度 = 模型高度，半徑取較窄那一邊的 35%
                Vector3 localCenter = transform.InverseTransformPoint(b.center);
                capsule.direction = 1;
                capsule.height = b.size.y;
                capsule.center = localCenter;
                capsule.radius = Mathf.Clamp(Mathf.Min(b.size.x, b.size.z) * 0.35f, 0.3f, b.size.y * 0.5f);
            }

            Debug.Log($"[FoolishBoss] 模型實際大小：高 {b.size.y:F3}m / 寬 {b.size.x:F3}m / 深 {b.size.z:F3}m" +
                      (autoFitModel ? $"（已縮放到 {targetHeight:F2}m）" : "（維持原始尺寸）") +
                      (autoFitCollider ? "，碰撞膠囊已貼合模型" : ""));
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
            if (_evilZone != null) _evilZone.Initialize(Arena.arenaCenter, Arena.arenaHalfSize + 0.5f);

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
                    if (_zoneRound + 1 < Arena.zoneRounds)
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
            SetPhase(FoolishCyclePhase.ZoneShow, Arena.zoneShowDuration);
            if (_evilZone != null) _evilZone.Show(1f);
            ActivatePlannedZones(); // 預告的那批善區現在正式生效
        }

        private void EnterBuffer()
        {
            SetPhase(FoolishCyclePhase.Buffer, Arena.bufferDuration);
            HideAllSafeZones();
            if (_evilZone != null) _evilZone.Hide();

            // 緩衝之後還有下一輪善惡區：現在就預告它的位置；之後是打鬥窗口就不預告
            if (_zoneRound + 1 < Arena.zoneRounds) PlanNextRound();
        }

        private void EnterBattle()
        {
            SetPhase(FoolishCyclePhase.Battle, Arena.battleDuration);
            HideAllSafeZones();
            if (_evilZone != null) _evilZone.Hide();
        }

        private void EnterRoar()
        {
            SetPhase(FoolishCyclePhase.Roar, Arena.roarDuration);
            HideAllSafeZones();
            if (_evilZone != null) _evilZone.Hide();

            // 下一個大循環第一輪善區的預告：嘶吼和擴散期間都看得到
            PlanNextRound();

            // 先讓 AI 回到待機（停掉進行中的攻擊/移動），再播嘶吼；
            // 不然攻擊結束時會觸發 Idle，把播到一半的嘶吼動畫切掉。
            if (CurrentStateType != FoolishBossStateType.Idle) _fsm.Force(FoolishBossStateType.Idle);
            PlayAnimTrigger("Roar");
            Feedback.Play(_profile.roar, _profile.roar.GetDuration(Arena.roarDuration));
        }

        private void EnterSpread()
        {
            SetPhase(FoolishCyclePhase.Spread, Arena.spreadDuration);
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
            float baseRadius = Arena.safeZoneBaseRadius > 0f ? Arena.safeZoneBaseRadius : zone.GetSceneRadius();
            return baseRadius + Arena.safeZoneExtraRadiusAtZeroHp * (1f - HpRatio);
        }

        /// <summary>
        /// 假隨機：數量在 min~max 之間均勻抽，但數量是最少或最多時，若已連續 maxSameCountStreak 輪相同就排除，
        /// 避免連續好幾輪都是 1 個或 3 個。
        /// </summary>
        private int RollSafeZoneCount()
        {
            int min = Mathf.Max(1, Arena.safeZoneMin);
            int max = Mathf.Max(min, Arena.safeZoneMax);

            var candidates = new List<int>();
            for (int n = min; n <= max; n++)
            {
                bool isExtreme = n == min || n == max;
                bool blocked = isExtreme && n == _lastSafeCount && _sameCountStreak >= Arena.maxSameCountStreak;
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
            float range = Mathf.Max(0f, Arena.arenaHalfSize - Arena.edgeMargin - radius);
            Vector2 candidate = Arena.arenaCenter;

            for (int attempt = 0; attempt < 30; attempt++)
            {
                candidate = Arena.arenaCenter + new Vector2(
                    UnityEngine.Random.Range(-range, range),
                    UnityEngine.Random.Range(-range, range));

                bool ok = true;
                for (int i = 0; i < centers.Count; i++)
                {
                    if (Vector2.Distance(candidate, centers[i]) < radius + radii[i] + Arena.safeZoneGap)
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

            // 傷害倍率依階段：
            //   打鬥窗口 = 全額；惡區出現中（善惡區階段）= evilZoneDamageMultiplier 加成（冒險輸出的獎勵）；
            //   緩衝 / 嘶吼 / 擴散 = evadeDamageMultiplier 減傷，而且打不死（最後一擊留給打鬥窗口或惡區階段）。
            // 勾選 vulnerableOutsideBattle = 緩衝 / 嘶吼 / 擴散也全額。
            if (CyclePhase == FoolishCyclePhase.ZoneShow)
                amount *= Arena.evilZoneDamageMultiplier;
            bool evading = CyclePhase != FoolishCyclePhase.Battle && CyclePhase != FoolishCyclePhase.ZoneShow
                           && !Arena.vulnerableOutsideBattle;
            if (evading)
                amount *= Arena.evadeDamageMultiplier;
            if (amount <= 0f) return;

            float ratioBefore = HpRatio;
            CurrentHp = Mathf.Max(evading ? Mathf.Min(1f, CurrentHp) : 0f, CurrentHp - amount);

            if (CurrentHp <= 0f)
            {
                _fsm.Force(FoolishBossStateType.Dead);
                return;
            }

            // 受擊回饋疊加在目前動作上（不中斷攻擊的命中判定）；配音有最小間隔，避免連續被打時一直喊
            bool playVoice = Time.time >= _nextHurtVoiceTime;
            if (playVoice) _nextHurtVoiceTime = Time.time + _profile.hurtVoiceMinInterval;
            Feedback.Play(_profile.hurt, _profile.hurt.GetDuration(_profile.staggerDuration), interrupt: false, playVoice: playVoice);

            // 打鬥窗口中血量降到門檻以下 → 窗口提前結束（每場戰鬥只觸發一次）
            if (CyclePhase == FoolishCyclePhase.Battle && !_earlyEndUsed
                && ratioBefore > Arena.battleEarlyEndHpRatio && HpRatio <= Arena.battleEarlyEndHpRatio)
            {
                _earlyEndUsed = true;
                EnterRoar();
                return; // 已經開始嘶吼，不要再被硬直打斷
            }

            if (EnableStagger)
                _fsm.Request(FoolishBossStateType.Stagger);
        }

        /// <summary>
        /// 被特殊技打中：不管「受擊硬直」有沒有勾，都中斷目前動作、短暫硬直（時間 = 素材總表的受擊硬直秒數）。
        /// 嘶吼中不打斷（嘶吼是惡區要回來的信號，被打斷會讓玩家看不到）。
        /// </summary>
        public void Interrupt()
        {
            if (IsDead || CyclePhase == FoolishCyclePhase.Roar) return;
            _fsm.Force(FoolishBossStateType.Stagger);
        }

        public void MoveTowardPlayer(float dt)
        {
            if (_player == null) return;
            Vector3 dir = (_player.position - transform.position).normalized;
            dir.y = 0f;
            transform.position += dir * _profile.moveSpeed * dt;
            if (dir != Vector3.zero)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                                                       Quaternion.LookRotation(dir), 10f * dt);
        }

        /// <summary>直接轉向主角（攻擊開始時用，讓招式朝主角出手）。</summary>
        public void FacePlayer()
        {
            if (_player == null) return;
            Vector3 dir = _player.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir);
        }

        public float GetAttackDamage(FoolishBossStateType type) => _profile.GetAttack(type).damage;

        /// <summary>
        /// 攻擊命中判定（在素材總表設定的命中時間點呼叫一次）：
        /// 主角在命中距離內、且在 Boss 正前方的扇形角度內就扣血。翻滾無敵由 PlayerController.TakeDamage 自己擋。
        /// </summary>
        public void PerformAttackHit(FoolishBossStateType type)
        {
            if (IsDead || BossResultPortal.IsPlayerDefeated || _player == null || _playerController == null) return;
            if (CurrentStateType != type) return; // 已經離開這招（被打斷、死亡）就不判定

            var attack = _profile.GetAttack(type);
            Vector3 toPlayer = _player.position - transform.position;
            toPlayer.y = 0f;
            float dist = toPlayer.magnitude;
            float angle = toPlayer.sqrMagnitude > 0.0001f ? Vector3.Angle(transform.forward, toPlayer) : 0f;
            bool hit = dist <= attack.hitRadius && (attack.hitAngle >= 360f || angle <= attack.hitAngle * 0.5f);

            if (_profile.showHitArea)
            {
                ShowHitArea(attack, hit);
                Debug.Log($"[癡] {type} 命中判定（時間點 {attack.hitNormalizedTime:P0}）：{(hit ? "打到" : "沒打到")}" +
                          $"　主角距離 {dist:F1}m / 判定距離 {attack.hitRadius:F1}m　偏離正前方 {angle:F0}° / 判定角度 ±{attack.hitAngle * 0.5f:F0}°", this);
            }

            if (hit) _playerController.TakeDamage(attack.damage);
        }

        /// <summary>測試用：在地上畫出這次的判定範圍（扇形外框），0.4 秒後消失。紅 = 打到、黃 = 沒打到。</summary>
        private void ShowHitArea(BossActionEntry attack, bool hit)
        {
            var go = new GameObject("癡_命中判定範圍(測試)");
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = attack.hitAngle < 360f;
            lr.widthMultiplier = 0.12f;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = lr.endColor = hit ? new Color(1f, 0.15f, 0.15f) : new Color(1f, 0.85f, 0.1f);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            const int segments = 32;
            float half = Mathf.Min(attack.hitAngle, 360f) * 0.5f;
            Vector3 origin = transform.position + Vector3.up * 0.05f;
            var points = new List<Vector3>();
            if (attack.hitAngle < 360f) points.Add(origin);
            for (int i = 0; i <= segments; i++)
            {
                float a = -half + half * 2f * i / segments;
                points.Add(origin + Quaternion.Euler(0f, a, 0f) * transform.forward * attack.hitRadius);
            }
            lr.positionCount = points.Count;
            lr.SetPositions(points.ToArray());

            Destroy(lr.material, 0.4f);
            Destroy(go, 0.4f);
        }

        /// <summary>觸發 Animator 動作（先清掉其他還沒被消耗的 Trigger，避免殘留的 Trigger 讓動畫亂跳）。</summary>
        public void PlayAnimTrigger(string trigger)
        {
            if (Animator == null || Animator.runtimeAnimatorController == null) return;
            foreach (var p in AnimTriggers) Animator.ResetTrigger(p);
            Animator.SetTrigger(trigger);
        }

        private static readonly string[] AnimTriggers = { "Idle", "Move", "Attack1", "Attack2", "Attack3", "Roar", "Stagger", "Dead" };

        public void OnDeath()
        {
            HideAllSafeZones();
            if (_evilZone) _evilZone.Hide();
            BossResultPortal.Instance?.OnBossDefeated(_profile.dead.GetDuration(0f));   // 等死亡動畫播完才跑通關流程
        }

        private void OnDrawGizmosSelected()
        {
            if (_profile == null) return;
            // 三招的命中範圍：紅=攻擊1、橘=攻擊2、黃=攻擊3
            var colors = new[] { new Color(1f, 0.2f, 0.2f), new Color(1f, 0.6f, 0.1f), new Color(1f, 0.9f, 0.2f) };
            var attacks = new[] { _profile.attack1, _profile.attack2, _profile.attack3 };
            for (int i = 0; i < 3; i++)
            {
                Gizmos.color = colors[i];
                float r = attacks[i].hitRadius;
                float half = Mathf.Min(attacks[i].hitAngle, 360f) * 0.5f;
                Vector3 prev = transform.position + Quaternion.Euler(0f, -half, 0f) * transform.forward * r;
                for (int s = 1; s <= 24; s++)
                {
                    Vector3 next = transform.position + Quaternion.Euler(0f, -half + half * 2f * s / 24f, 0f) * transform.forward * r;
                    Gizmos.DrawLine(prev, next);
                    prev = next;
                }
                if (half < 180f)
                {
                    Gizmos.DrawLine(transform.position, transform.position + Quaternion.Euler(0f, -half, 0f) * transform.forward * r);
                    Gizmos.DrawLine(transform.position, transform.position + Quaternion.Euler(0f, half, 0f) * transform.forward * r);
                }
            }
        }
    }
}
