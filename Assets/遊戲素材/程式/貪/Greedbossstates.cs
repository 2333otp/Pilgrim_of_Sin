using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    // ════════════════════════════════════════════════════════════════
    //  IdleState：待機，時間到從 GreedBossProfile 的起手招裡挑一招
    // ════════════════════════════════════════════════════════════════
    public class GreedIdleState : GreedBossState
    {
        public override GreedBossStateType StateType => GreedBossStateType.Idle;

        private float _timer;
        private float _elapsed;

        public GreedIdleState(GreedBossController b, GreedBossStateMachine m) : base(b, m) { }

        public override void Enter()
        {
            _timer = Boss.ConsumeNextIdleDuration();
            _elapsed = 0f;
            PlayAnim();
            Boss.Feedback.Begin(Boss.Profile.idle);
        }

        public override void Exit() => Boss.Feedback.Stop();

        public override void Update(float dt)
        {
            if (Boss.IsDead) { ForceGo(GreedBossStateType.Dead); return; }
            _elapsed += dt;
            Boss.Feedback.Tick(_elapsed / Mathf.Max(0.01f, Boss.Profile.idle.GetDuration(1f)));
            if (Boss.IsInBalanceWindow) return;

            Boss.FacePlayer(dt);   // 待機時持續面向玩家，出招時才不會對著空氣揮

            _timer -= dt;
            if (_timer > 0f) return;

            var move = Boss.PickOpener();
            if (move != null)
            {
                Boss.PendingMove = move;
                Go(GreedBossStateType.Action);
            }
            else if (!Boss.AnyOpenerInRange())
                Go(GreedBossStateType.Move);
            else
                _timer = 0.2f;   // 距離夠但招式都在冷卻 / 相位不符：稍後再挑
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  MoveState：追擊，進入任一起手招的距離就停下來
    // ════════════════════════════════════════════════════════════════
    public class GreedMoveState : GreedBossState
    {
        public override GreedBossStateType StateType => GreedBossStateType.Move;

        private float _elapsed;

        public GreedMoveState(GreedBossController b, GreedBossStateMachine m) : base(b, m) { }

        public override void Enter()
        {
            _elapsed = 0f;
            PlayAnim();
            Boss.Feedback.Begin(Boss.Profile.move);
        }

        public override void Exit() => Boss.Feedback.Stop();

        public override void FixedUpdate(float dt)
        {
            if (Boss.IsDead) { ForceGo(GreedBossStateType.Dead); return; }
            _elapsed += dt;
            Boss.Feedback.Tick(_elapsed / Mathf.Max(0.01f, Boss.Profile.move.GetDuration(1f)));
            if (Boss.IsInBalanceWindow) { Go(GreedBossStateType.Idle); return; }

            Boss.MoveTowardPlayer(dt);

            if (Boss.AnyOpenerInRange())
            {
                Boss.SetNextIdleDuration(Boss.IdleDuration);
                Go(GreedBossStateType.Idle);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  ActionState：播一招（GreedMoveEntry），處理瞄準、突進、命中區間與派生
    // ════════════════════════════════════════════════════════════════
    public class GreedActionState : GreedBossState
    {
        public override GreedBossStateType StateType => GreedBossStateType.Action;

        private GreedMoveEntry _move;
        private float _time;
        private float _duration;
        private bool[] _windowDone;
        private bool[] _windowHit;
        private bool[] _branchChecked;
        private bool _fanDone;

        public GreedMoveEntry CurrentMove => _move;
        public float Progress => _duration > 0f ? Mathf.Clamp01(_time / _duration) : 1f;

        public GreedActionState(GreedBossController b, GreedBossStateMachine m) : base(b, m) { }

        public override void Enter()
        {
            _move = Boss.PendingMove;
            Boss.PendingMove = null;
            _time = 0f;

            if (_move == null || !_move.IsValid)
            {
                Debug.LogWarning("[GreedAction] 沒有可播的招式，回到待機。");
                _duration = 0f;
                return;
            }

            _duration = _move.Duration;
            _windowDone = new bool[_move.hitWindows.Count];
            _windowHit = new bool[_move.hitWindows.Count];
            _branchChecked = new bool[_move.branches.Count];
            _fanDone = false;
            Boss.MarkMoveUsed(_move);
            Boss.PlayAnimState(_move.stateName, _move.speed);
            Boss.Feedback.Begin(_move);
        }

        public override void Update(float dt)
        {
            if (Boss.IsDead) { ForceGo(GreedBossStateType.Dead); return; }
            if (_move == null || _duration <= 0f) { Go(GreedBossStateType.Idle); return; }

            float prev = Progress;
            _time += dt;
            float p = Progress;

            Boss.Feedback.Tick(p);
            if (p < _move.trackUntilRatio) Boss.FacePlayer(dt, _move.turnSpeed);

            // 突進：把 lungeStart~lungeEnd 區間內這一幀經過的比例換算成距離
            if (_move.lungeDistance > 0f && _move.lungeEnd > _move.lungeStart)
            {
                float a = Mathf.Clamp(prev, _move.lungeStart, _move.lungeEnd);
                float b = Mathf.Clamp(p, _move.lungeStart, _move.lungeEnd);
                if (b > a) Boss.LungeStep(_move.lungeDistance * (b - a) / (_move.lungeEnd - _move.lungeStart));
            }

            // 命中區間：每一下最多打中一次。用「這一幀跨過的範圍」判斷，掉幀時短區間也不會被跳過。
            for (int i = 0; i < _windowDone.Length; i++)
            {
                if (_windowDone[i]) continue;
                var w = _move.hitWindows[i];
                if (p < w.start || prev >= w.end) continue;
                float dmg = GreedHitWindow.DamageAt(_move.hitWindows, i, _move.damage);
                Vector3 center = Boss.LocalToWorldHitCenter(w.forward, w.height);
                if (Boss.TryHitPlayerSphere(center, w.radius, dmg))
                    _windowDone[i] = _windowHit[i] = true;
                if (p >= w.end) _windowDone[i] = true;
                if (_windowDone[i]) Boss.ReportHitSphere(center, w.radius, _windowHit[i], $"{_move.stateName} 第 {i + 1} 下");
            }

            // 沒有命中區間：和癡一樣，在命中時間點做一次扇形判定
            if (!_fanDone && _move.UsesFanHit && p >= _move.hitNormalizedTime)
            {
                _fanDone = true;
                Boss.TryHitPlayerFan(_move.hitRadius, _move.hitAngle, _move.damage, _move.stateName);
            }

            // 派生：由上往下，播到 atRatio 時檢查一次；第一個成立的就接
            for (int i = 0; i < _branchChecked.Length; i++)
            {
                if (_branchChecked[i]) continue;
                var br = _move.branches[i];
                if (p < br.atRatio) continue;
                _branchChecked[i] = true;

                if (!Boss.EvaluateCondition(br.condition)) continue;
                if (Random.value > br.chance) continue;
                var next = Boss.Profile != null ? Boss.Profile.FindMove(br.toMove) : null;
                if (next == null || !next.IsValid)
                {
                    Debug.LogWarning($"[GreedAction] {_move.stateName} 的派生目標「{br.toMove}」不存在或沒放動畫。");
                    continue;
                }
                Boss.PendingMove = next;
                Go(GreedBossStateType.Action);   // 同狀態重進：Exit → Enter 會重置計時
                return;
            }

            if (p >= 1f)
            {
                Boss.SetNextIdleDuration(_move.recoveryIdle);
                Go(GreedBossStateType.Idle);
            }
        }

        public override void Exit()
        {
            Boss.SetAnimSpeed(1f);
            Boss.Feedback.Stop();   // 派生時下一招 Enter 會再 Begin 自己的
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  KickScaleState（平衡結束空襲）
    //  起飛（停格等飛到定位）→ 天秤一側踢翻 → 俯衝鎖定主角（停格等落地）→ 落地爬起 → 重置天秤
    // ════════════════════════════════════════════════════════════════
    public class GreedKickScaleState : GreedBossState
    {
        public override GreedBossStateType StateType => GreedBossStateType.KickScale;

        private enum Phase { TakeOff, Kick, Dive, Fall, Legacy }

        private Phase _phase;
        private float _animTime;       // 目前這段動畫已播的秒數（停格時不累加）
        private float _animDuration;
        private bool _holding;

        // 飛行路徑：任意參數曲線 s∈[0,1] → 世界座標，用弧長表換算成等速前進
        private const int ArcSamples = 160;
        private readonly float[] _arc = new float[ArcSamples + 1];
        private System.Func<float, Vector3> _path;
        private Vector3 _to;
        private float _pathLen, _pathDist, _travelTime;
        private bool _travelling, _arrived;
        private float _roll;              // 目前傾斜角（轉彎內傾）
        private float _pitch;             // 目前前趴角度
        private Vector3 _lastTangent;
        private float _loopClock;          // 盤旋時循環區段的計時

        private Vector3 _kickFace;
        private bool _kickFired;
        private Vector3 _diveTarget;
        private bool _targetLocked;

        // 舊流程（沒有設定空襲動畫時）
        private bool _legacyDone;
        private float _legacyTimer;

        public string PhaseName => _phase.ToString();

        public GreedKickScaleState(GreedBossController b, GreedBossStateMachine m) : base(b, m) { }

        private GreedAerialKickConfig Cfg => Boss.Profile != null ? Boss.Profile.aerialKick : null;

        public override void Enter()
        {
            var c = Cfg;
            if (c == null || !c.IsComplete)
            {
                // 沒設定空襲：沿用舊流程（Boss 站著，天秤自己翻倒）
                _phase = Phase.Legacy;
                _legacyDone = false;
                _legacyTimer = 0f;
                PlayAnim();
                Boss.OnKickScaleAnimEnd += HandleLegacyEnd;
                Boss.PlayScaleBreak();
                return;
            }

            Boss.BeginAirborne();
            Boss.GetKickPoint(out _to, out _kickFace);
            _kickFired = false;
            _targetLocked = false;
            StartPhase(Phase.TakeOff);
        }

        public override void Exit()
        {
            Boss.OnKickScaleAnimEnd -= HandleLegacyEnd;
            Boss.SetAnimSpeed(1f);
            Boss.Feedback.Stop();
            Boss.EndAirborne();   // 被強制中斷（死亡等）時也要回到地面
        }

        private void HandleLegacyEnd() => _legacyDone = true;

        private void StartPhase(Phase p)
        {
            var c = Cfg;
            _phase = p;
            _animTime = 0f;
            _holding = false;
            switch (p)
            {
                case Phase.TakeOff:
                    _animDuration = c.FlyDuration;
                    _travelling = false; _arrived = false;
                    break;
                case Phase.Kick:
                    _animDuration = c.KickDuration;
                    break;
                case Phase.Dive:
                    _animDuration = c.DiveDuration;
                    _travelling = false; _arrived = false;
                    break;
                case Phase.Fall:
                    _animDuration = c.FallDuration;
                    break;
            }
            var entry = CurrentEntry();
            Boss.PlayAnimState(entry.stateName, CurrentSpeedMultiplier());
            Boss.Feedback.Begin(entry);
        }

        private BossActionEntry CurrentEntry()
        {
            var c = Cfg;
            return _phase switch
            {
                Phase.TakeOff => c.fly,
                Phase.Kick => c.kick,
                Phase.Dive => c.dive,
                _ => c.fall,
            };
        }

        private float Progress => _animDuration > 0f ? Mathf.Clamp01(_animTime / _animDuration) : 1f;

        /// <summary>推進動畫計時；到停格幀且還沒到位就凍結動畫。</summary>
        private void AdvanceAnim(float dt, float holdRatio, bool canPassHold, float loopEndRatio = 0f)
        {
            if (!canPassHold && Progress >= holdRatio)
            {
                if (!_holding) { _holding = true; _loopClock = 0f; Boss.SetAnimSpeed(0f); }

                // 循環區段：在 停格幀 ~ loopEndRatio 之間來回播（手動推 Animator 時間）
                if (loopEndRatio > holdRatio && _animDuration > 0f)
                {
                    _loopClock += dt;
                    float seg = (loopEndRatio - holdRatio) * _animDuration;
                    float t = holdRatio + Mathf.PingPong(_loopClock, seg) / _animDuration;
                    Boss.ScrubAnimState(CurrentStateName(), t);
                }
                return;
            }
            if (_holding)
            {
                _holding = false;
                if (loopEndRatio > holdRatio) Boss.ScrubAnimState(CurrentStateName(), Progress);   // 從停格幀接著播
                Boss.SetAnimSpeed(CurrentSpeedMultiplier());
            }
            _animTime += dt;
        }

        private string CurrentStateName() => CurrentEntry().stateName;

        private float CurrentSpeedMultiplier()
        {
            var c = Cfg;
            return _phase switch
            {
                Phase.TakeOff => c.flySpeedMultiplier,
                Phase.Kick => c.kickSpeedMultiplier,
                Phase.Dive => c.diveSpeedMultiplier,
                _ => c.fallSpeedMultiplier,
            };
        }

        // ── 路徑 ──────────────────────────────────────────────────────

        private void BeginTravel(System.Func<float, Vector3> path, Vector3 to)
        {
            _path = path;
            _to = to;
            _arc[0] = 0f;
            Vector3 prev = path(0f);
            for (int i = 1; i <= ArcSamples; i++)
            {
                Vector3 p = path((float)i / ArcSamples);
                _arc[i] = _arc[i - 1] + Vector3.Distance(prev, p);
                prev = p;
            }
            _pathLen = Mathf.Max(0.01f, _arc[ArcSamples]);
            _pathDist = 0f;
            _travelTime = 0f;
            _travelling = true;
            _arrived = false;
            _lastTangent = Vector3.zero;
            _roll = 0f;
            _pitch = 0f;
        }

        /// <summary>弧長 → 曲線參數 s（線性插值查表）。</summary>
        private float SAtDistance(float d)
        {
            if (d <= 0f) return 0f;
            if (d >= _pathLen) return 1f;
            int lo = 0, hi = ArcSamples;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (_arc[mid] < d) lo = mid; else hi = mid;
            }
            float seg = _arc[hi] - _arc[lo];
            float f = seg > 0f ? (d - _arc[lo]) / seg : 0f;
            return (lo + f) / ArcSamples;
        }

        /// <summary>直線（俯衝用）。</summary>
        private static System.Func<float, Vector3> LinePath(Vector3 a, Vector3 b) => s => Vector3.Lerp(a, b, s);

        /// <summary>
        /// 盤旋路徑：以天秤為中心，從目前位置一邊爬升一邊拉開到盤旋半徑，繞至少 orbitDegrees，
        /// 最後一段滑翔下降、收半徑切入定位點。角度隨 s 線性前進，半徑 / 高度在前後段用 smoothstep 過渡。
        /// </summary>
        private System.Func<float, Vector3> OrbitPath(Vector3 from, Vector3 to, Vector3 center, GreedAerialKickConfig c)
        {
            Vector3 f = from - center; f.y = 0f;
            Vector3 t = to - center; t.y = 0f;
            float r0 = f.magnitude, r1 = t.magnitude;
            float a0 = Mathf.Atan2(f.z, f.x), a1 = Mathf.Atan2(t.z, t.x);

            const float Tau = Mathf.PI * 2f;
            float ccw = Mathf.Repeat(a1 - a0, Tau);    // 逆時針（俯視，角度增加）需要的角度
            float cw = Mathf.Repeat(a0 - a1, Tau);     // 順時針需要的角度
            float minSweep = c.orbitDegrees * Mathf.Deg2Rad;
            float ExtendTo(float baseSweep) { while (baseSweep < minSweep) baseSweep += Tau; return baseSweep; }

            float sweepCcw = ExtendTo(ccw), sweepCw = ExtendTo(cw);
            float sweep = c.orbitDirection switch
            {
                GreedOrbitDirection.逆時針 => sweepCcw,
                GreedOrbitDirection.順時針 => -sweepCw,
                _ => sweepCcw <= sweepCw ? sweepCcw : -sweepCw,
            };

            float R = c.orbitRadius;
            float yCruise = Boss.GroundY + c.cruiseHeight;
            float y0 = from.y, y1 = to.y;
            float climb = c.climbPortion, glide = c.glidePortion;
            float sg = 1f - glide;
            float m = 1f / (1f - glide * 2f / 3f);

            // 角度進度：前段等角速度；滑翔段角速度以 (1-x)² 減到 0。
            // 半徑收縮速度在終點是以 (1-x) 歸零，比角速度慢一階，所以最後是沿半徑朝天秤直線切入，
            // 到位時剛好面朝天秤，不用急轉。（積分：m·sg + m·glide/3 = 1）
            float AngleProgress(float s)
            {
                if (s < sg) return m * s;
                float x = (s - sg) / glide, u = 1f - x;
                return m * sg + m * glide * (1f - u * u * u) / 3f;
            }

            return s =>
            {
                float ang = a0 + sweep * AngleProgress(s);
                float r, y;
                if (s < climb)
                {
                    float k = Mathf.SmoothStep(0f, 1f, s / climb);
                    r = Mathf.Lerp(r0, R, k);
                    y = Mathf.Lerp(y0, yCruise, Mathf.Sin(k * Mathf.PI * 0.5f));   // 前段爬得快（ease-out）
                }
                else if (s > 1f - glide)
                {
                    float k = Mathf.SmoothStep(0f, 1f, (s - (1f - glide)) / glide);
                    r = Mathf.Lerp(R, r1, k);
                    y = Mathf.Lerp(yCruise, y1, k);
                }
                else { r = R; y = yCruise; }
                return new Vector3(center.x + Mathf.Cos(ang) * r, y, center.z + Mathf.Sin(ang) * r);
            };
        }

        /// <summary>沿路徑等速前進，機頭朝切線、轉彎時往內側傾斜、身體前趴 pitch 度；回傳是否到達。</summary>
        private bool StepTravel(float dt, float speed, float maxBank, float pitch = 0f)
        {
            if (!_travelling) return _arrived;
            _travelTime += dt;
            _pathDist = Mathf.Min(_pathLen, _pathDist + speed * dt);
            if (_travelTime >= Cfg.maxTravelSeconds) _pathDist = _pathLen;

            float s = SAtDistance(_pathDist);
            Vector3 pos = _path(s);
            Quaternion rot = Boss.transform.rotation;

            // 切線（往前看一小段）
            Vector3 ahead = _path(Mathf.Min(1f, s + 0.01f)) - pos;
            if (s >= 0.99f) ahead = pos - _path(0.98f);
            Vector3 tangent = new Vector3(ahead.x, 0f, ahead.z);
            if (tangent.sqrMagnitude > 0.000001f && dt > 0f)
            {
                tangent.Normalize();
                // 轉彎角速度（度/秒，俯視順時針 = 右轉 = 正）→ 傾斜角；繞前方軸 +z 旋轉會抬起右翼，所以右轉用 -roll（右翼往下）
                float turnRate = _lastTangent == Vector3.zero ? 0f : Vector3.SignedAngle(_lastTangent, tangent, Vector3.up) / dt;
                float targetRoll = Mathf.Clamp(turnRate / 60f * maxBank, -maxBank, maxBank);
                _roll = Mathf.Lerp(_roll, targetRoll, 1f - Mathf.Exp(-4f * dt));
                // 前趴：離地後慢慢壓下去，最後滑翔切入定位點那一小段再慢慢抬回來（到位後要回正踢）
                float targetPitch = s > 0.9f ? pitch * Mathf.InverseLerp(1f, 0.9f, s) : pitch;
                _pitch = Mathf.Lerp(_pitch, targetPitch, 1f - Mathf.Exp(-4f * dt));
                _lastTangent = tangent;
                // 平滑轉向（剛離地時切線方向可能和原本朝向差很多，不要瞬間轉過去）；+X 旋轉 = 頭朝下
                Quaternion target = Quaternion.LookRotation(tangent) * Quaternion.Euler(_pitch, 0f, -_roll);
                rot = Quaternion.Slerp(rot, target, 1f - Mathf.Exp(-10f * dt));
            }
            Boss.SetAirPose(pos, rot);

            if (_pathDist >= _pathLen) { _travelling = false; _arrived = true; }
            return _arrived;
        }

        public override void Update(float dt)
        {
            if (Boss.IsDead) { ForceGo(GreedBossStateType.Dead); return; }
            var c = Cfg;

            switch (_phase)
            {
                case Phase.Legacy:
                    _legacyTimer += dt;
                    if (_legacyDone || _legacyTimer >= 4f) { Boss.ResetScale(); Go(GreedBossStateType.Idle); }
                    return;

                case Phase.TakeOff:
                    if (!_travelling && !_arrived && Progress >= Mathf.Min(c.liftOffRatio, c.flyHoldRatio))
                        BeginTravel(OrbitPath(Boss.transform.position, _to, Boss.ScaleCenter, c), _to);
                    if (_travelling) StepTravel(dt, c.flySpeed, c.bankAngle, c.flyPitch);
                    if (_arrived)   // 到位後收起傾斜 / 前趴、轉向天秤（以身體中心為軸回正）
                        Boss.SetAirPose(_to, Quaternion.Slerp(Boss.transform.rotation, Quaternion.LookRotation(_kickFace), 1f - Mathf.Exp(-8f * dt)));
                    AdvanceAnim(dt, c.flyHoldRatio, _arrived, c.flyLoopEndRatio);
                    Boss.Feedback.Tick(Progress);
                    if (Progress >= 1f) StartPhase(Phase.Kick);
                    break;

                case Phase.Kick:
                    Boss.FaceDirection(_kickFace, dt, 12f);
                    _animTime += dt;
                    Boss.Feedback.Tick(Progress);
                    if (!_kickFired && Progress >= c.kickImpactRatio)
                    {
                        _kickFired = true;
                        Boss.PlayScaleBreak();
                    }
                    if (Progress >= 1f)
                    {
                        if (!c.lockTargetAtLaunch) LockTarget();
                        StartPhase(Phase.Dive);
                    }
                    break;

                case Phase.Dive:
                    if (!_travelling && !_arrived)
                    {
                        Boss.FacePlayer(dt, 12f);   // 起跳前持續瞄準
                        if (Progress >= Mathf.Min(c.diveLaunchRatio, c.diveHoldRatio))
                        {
                            if (!_targetLocked) LockTarget();
                            BeginTravel(LinePath(Boss.transform.position, _diveTarget), _diveTarget);   // 直線往下衝
                        }
                    }
                    if (_travelling && StepTravel(dt, c.diveSpeed, 0f))
                    {
                        Boss.Land();
                        Boss.ImpactAoE(c.impactRadius, c.impactDamage);
                        StartPhase(Phase.Fall);
                        break;
                    }
                    AdvanceAnim(dt, c.diveHoldRatio, false);
                    Boss.Feedback.Tick(Progress);
                    break;

                case Phase.Fall:
                    _animTime += dt;
                    Boss.Feedback.Tick(Progress);
                    if (Progress >= 1f)
                    {
                        Boss.ResetScale();
                        Boss.SetNextIdleDuration(Boss.IdleDuration);
                        Go(GreedBossStateType.Idle);
                    }
                    break;
            }
        }

        private void LockTarget()
        {
            _diveTarget = Boss.GetDiveTarget(Cfg.maxLeadDistance);
            _targetLocked = true;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  StaggerState
    //  選配：需在素材總表勾「受擊硬直」
    // ════════════════════════════════════════════════════════════════
    public class GreedStaggerState : GreedBossState
    {
        public override GreedBossStateType StateType => GreedBossStateType.Stagger;

        private float _timer;

        public GreedStaggerState(GreedBossController b, GreedBossStateMachine m) : base(b, m) { }

        public override void Enter()
        {
            _timer = Boss.StaggerDuration;
            PlayAnim();
        }

        public override void Update(float dt)
        {
            if (Boss.IsDead) { ForceGo(GreedBossStateType.Dead); return; }
            _timer -= dt;
            if (_timer <= 0f)
            {
                Boss.SetNextIdleDuration(Boss.IdleDuration);
                Go(GreedBossStateType.Idle);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  DeadState
    // ════════════════════════════════════════════════════════════════
    public class GreedDeadState : GreedBossState
    {
        public override GreedBossStateType StateType => GreedBossStateType.Dead;

        private float _elapsed;

        public GreedDeadState(GreedBossController b, GreedBossStateMachine m) : base(b, m) { }

        public override void Enter()
        {
            Boss.EndAirborne();
            PlayAnim();
            Boss.OnDeath();
            _elapsed = 0f;
            Boss.Feedback.Begin(Boss.Profile.dead);   // 死亡的音效 / 配音 / 特效（時間點 = 死亡動畫比例）
        }

        public override void Update(float dt)
        {
            _elapsed += dt;
            Boss.Feedback.Tick(_elapsed / Mathf.Max(0.01f, Boss.Profile.dead.GetDuration(2f)));
        }
    }
}
