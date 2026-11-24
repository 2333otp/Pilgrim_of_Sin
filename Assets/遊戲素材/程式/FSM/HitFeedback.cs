using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 打擊感：鏡頭晃動、頓幀、手把震動、受擊亮光。掛在主角 Prefab 上（有主角的場景都有）。
    ///
    /// 動暈考量（評審意見）：
    ///   · 晃動只做短促的「一下」，由強到弱衰減；只晃上下左右，不做畫面傾斜。
    ///   · 同時有多個晃動只取最強的那個，不相加，連段再快也不會越晃越大。
    ///   · 鎖定 Boss 時晃動減弱；強度乘上玩家設定（預設 70%，0 = 完全不晃）。
    ///   · 打擊感主要靠頓幀＋受擊亮光＋手把震動，關掉晃動打擊感也還在。
    ///
    /// 頓幀用 Time.timeScale 壓低（不是 0）：專案用 timeScale == 0 判斷暫停選單。
    /// 頓幀期間如果被別人改了 timeScale（例如按暫停），立刻放手、不再覆寫。
    /// </summary>
    [DisallowMultipleComponent]
    public class HitFeedback : MonoBehaviour
    {
        public static HitFeedback Instance { get; private set; }

        [SerializeField] private HitFeedbackSettings _settings;

        public HitFeedbackSettings Settings => _settings;

        // ── 鏡頭晃動 ──
        private float _shakeAmp, _shakeDur, _shakeTime;
        private float _noiseTime;

        // ── 時間控制（頓幀／慢動作） ──
        private float _hitstopLeft, _slowLeft;
        private bool _ownsTimeScale;
        private float _writtenTimeScale = 1f;
        private int _timeStartFrame = -1;

        // ── 手把震動 ──
        private Gamepad _rumblePad;
        private float _rumbleLeft, _rumbleLow, _rumbleHigh;

        // ── 受擊亮光 ──
        private class FlashState
        {
            public Renderer[] renderers;
            public int[] materialCount;          // 每個 renderer 有幾個材質槽
            public Color?[][] baseColors;        // 每個材質槽原本的 _BaseColor（null = 這個材質沒有顏色可調，只疊光暈）
            public uint[] originalLayers;        // 原本的渲染層（結束時還原）
            public float duration, elapsed, peak, peakGlow;
            public float lastStart = -999f;
            public bool active;
        }
        private readonly Dictionary<GameObject, FlashState> _flashes = new Dictionary<GameObject, FlashState>();
        private readonly List<GameObject> _flashKeys = new List<GameObject>();
        private readonly List<Renderer> _rendererBuffer = new List<Renderer>();

        // ════════════════════════════════════════════════════════════
        //  玩家設定
        // ════════════════════════════════════════════════════════════

        // PlayerPrefs 讀取很快，但晃動每幀都要讀，所以快取起來；設定頁改值時呼叫 RefreshPrefs()
        private static float? _shakeIntensity;
        private static bool? _rumbleEnabled;

        public static float ShakeIntensity => _shakeIntensity ??= HitFeedbackPrefs.ShakeIntensity;
        public static bool RumbleEnabled => _rumbleEnabled ??= HitFeedbackPrefs.RumbleEnabled;

        /// <summary>設置選單改了晃動強度／手把震動之後呼叫，讓新設定立刻生效。</summary>
        public static void RefreshPrefs()
        {
            _shakeIntensity = null;
            _rumbleEnabled = null;
            if (!RumbleEnabled) Instance?.StopRumble();
        }

        /// <summary>設置選單打開「手把震動」時給玩家一下試震（不受暫停影響）。</summary>
        public static void PreviewRumble()
        {
            if (!RumbleEnabled || Gamepad.current == null) return;
            // 不走 HitFeedback：ESC 選單開著時 timeScale = 0，HitFeedback 會把震動收掉；主選單也沒有主角
            PreviewRumbleRunner.Run(Gamepad.current, 0.35f, 0.5f, 0.15f);
        }

        /// <summary>試震用的小協程（真實時間計時，暫停中也會正常停止）。</summary>
        private class PreviewRumbleRunner : MonoBehaviour
        {
            private static PreviewRumbleRunner _instance;
            private Gamepad _pad;
            private float _left;

            public static void Run(Gamepad pad, float low, float high, float seconds)
            {
                if (_instance == null)
                {
                    var go = new GameObject("[HitFeedback Rumble Preview]") { hideFlags = HideFlags.HideAndDontSave };
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<PreviewRumbleRunner>();
                }
                _instance._pad = pad;
                _instance._left = seconds;
                pad.SetMotorSpeeds(low, high);
            }

            private void Update()
            {
                if (_pad == null) return;
                _left -= Time.unscaledDeltaTime;
                if (_left > 0f) return;
                Stop();
            }

            private void Stop()
            {
                try { _pad?.SetMotorSpeeds(0f, 0f); }
                catch (System.Exception) { /* 手把剛好拔掉 */ }
                _pad = null;
            }

            private void OnDisable() => Stop();
        }

        // 關閉 Domain Reload 進 Play Mode 時靜態快取不會自動清掉
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _shakeIntensity = null;
            _rumbleEnabled = null;
            Instance = null;
        }

        // ════════════════════════════════════════════════════════════
        //  對外介面（Instance 不存在時全部安靜略過）
        // ════════════════════════════════════════════════════════════

        /// <summary>主角打中目標：晃動＋頓幀＋手把震動＋目標亮一下。</summary>
        public static void PlayerHit(HitImpactLevel level, Component target)
        {
            var self = Instance;
            if (self == null) return;
            var tier = self.GetTier(level);
            if (tier == null) return;
            self.Shake(tier, 1f);
            self.Hitstop(tier);
            self.Rumble(tier, 1f);
            if (target != null) self.Flash(target.gameObject, tier.flashDuration, tier.flashIntensity, tier.flashGlow);
        }

        /// <summary>主角被打：晃動＋頓幀＋手把震動（主角自己不亮，已經有受擊動作）。</summary>
        public static void PlayerHurt(HitImpactLevel level)
        {
            var self = Instance;
            if (self == null) return;
            var tier = self.GetTier(level);
            if (tier == null) return;
            self.Shake(tier, 1f);
            self.Hitstop(tier);
            self.Rumble(tier, 1f);
        }

        /// <summary>
        /// Boss 重攻擊的環境衝擊（砸地、爆炸、踢天秤…）：沒打中也會晃，離越遠越小；不頓幀。
        /// 不震手把：手把震動只留給「我打中」和「我被打中」，躲開攻擊時不能讓玩家誤以為被打到
        /// （使用者實測回饋，2026-11-23）。真的被打中時 PlayerHurt 會自己震。
        /// </summary>
        public static void Environment(HitImpactLevel level, Vector3 source)
        {
            var self = Instance;
            if (self == null) return;
            var tier = self.GetTier(level);
            if (tier == null) return;
            float falloff = self.DistanceFalloff(source);
            if (falloff <= 0f) return;
            self.Shake(tier, falloff);
        }

        /// <summary>
        /// 鏡頭每幀呼叫：回傳要疊在鏡頭旋轉上的晃動（已乘上玩家設定與鎖定減弱）。
        /// </summary>
        public static Quaternion GetShakeRotation(bool lockedOn)
        {
            var self = Instance;
            if (self == null || self._settings == null) return Quaternion.identity;
            float amp = self.CurrentShakeAmplitude() * ShakeIntensity;
            if (lockedOn) amp *= self._settings.lockOnShakeMultiplier;
            if (amp <= 0.0001f) return Quaternion.identity;

            float t = self._noiseTime * self._settings.shakeFrequency;
            float pitch = (Mathf.PerlinNoise(t, 0.37f) * 2f - 1f) * amp;
            float yaw = (Mathf.PerlinNoise(7.31f, t) * 2f - 1f) * amp;
            return Quaternion.Euler(pitch, yaw, 0f);   // 不做 roll（畫面傾斜最容易暈）
        }

        // ════════════════════════════════════════════════════════════
        //  生命週期
        // ════════════════════════════════════════════════════════════

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[HitFeedback] 場景裡有兩個 HitFeedback，只使用第一個。", this);
                return;
            }
            Instance = this;
            if (_settings == null)
                Debug.LogWarning("[HitFeedback] 沒有指定打擊感設定檔（HitFeedbackSettings），打擊感不會作用。", this);
        }

        private void OnDisable() => ReleaseEverything();

        private void OnDestroy()
        {
            ReleaseEverything();
            if (Instance == this) Instance = null;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) StopRumble();
        }

        private void Update()
        {
            float udt = Time.unscaledDeltaTime;

            // 暫停選單（timeScale = 0 不是我們設的）：一切效果立刻收掉，不跟暫停搶 timeScale
            if (Time.timeScale == 0f)
            {
                _ownsTimeScale = false;
                _hitstopLeft = _slowLeft = 0f;
                _shakeAmp = 0f;
                StopRumble();
                EndAllFlashes();
                return;
            }

            UpdateTimeScale(udt);

            _noiseTime += udt;
            if (_shakeAmp > 0f)
            {
                _shakeTime += udt;
                if (_shakeTime >= _shakeDur) _shakeAmp = 0f;
            }

            if (_rumbleLeft > 0f)
            {
                _rumbleLeft -= udt;
                if (_rumbleLeft <= 0f) StopRumble();
            }

            UpdateFlashes(udt);
        }

        private void ReleaseEverything()
        {
            if (_ownsTimeScale && Time.timeScale == _writtenTimeScale) Time.timeScale = 1f;
            _ownsTimeScale = false;
            _hitstopLeft = _slowLeft = 0f;
            _shakeAmp = 0f;
            StopRumble();
            EndAllFlashes();
        }

        private HitImpactTier GetTier(HitImpactLevel level) => _settings != null ? _settings.Get(level) : null;

        // ════════════════════════════════════════════════════════════
        //  鏡頭晃動
        // ════════════════════════════════════════════════════════════

        private float CurrentShakeAmplitude()
        {
            if (_shakeAmp <= 0f || _shakeDur <= 0f) return 0f;
            float k = 1f - Mathf.Clamp01(_shakeTime / _shakeDur);
            return _shakeAmp * k * k;   // 一開始最強，很快收掉
        }

        private void Shake(HitImpactTier tier, float scale)
        {
            float amp = tier.shakeAngle * scale;
            if (amp <= 0f || tier.shakeDuration <= 0f) return;
            // 只取最強的：新的比目前剩下的還弱就不理，不會疊加
            if (amp < CurrentShakeAmplitude()) return;
            _shakeAmp = amp;
            _shakeDur = tier.shakeDuration;
            _shakeTime = 0f;
        }

        private float DistanceFalloff(Vector3 source)
        {
            float full = _settings.environmentFullRange;
            float max = Mathf.Max(full + 0.01f, _settings.environmentMaxRange);
            float d = Vector3.Distance(transform.position, source);
            if (d <= full) return 1f;
            return Mathf.Clamp01(1f - (d - full) / (max - full));
        }

        // ════════════════════════════════════════════════════════════
        //  頓幀／慢動作
        // ════════════════════════════════════════════════════════════

        private void Hitstop(HitImpactTier tier)
        {
            if (tier.hitstop <= 0f && tier.slowMotion <= 0f) return;
            if (Time.timeScale == 0f) return;                        // 暫停中
            if (!_ownsTimeScale && Time.timeScale != 1f) return;     // 別人正在控制時間，不搶

            _hitstopLeft = Mathf.Max(_hitstopLeft, tier.hitstop);
            _slowLeft = Mathf.Max(_slowLeft, tier.slowMotion);
            _timeStartFrame = Time.frameCount;
            _ownsTimeScale = true;
            ApplyTimeScale(DesiredTimeScale());
        }

        private float DesiredTimeScale()
        {
            if (_hitstopLeft > 0f) return _settings.hitstopTimeScale;
            if (_slowLeft > 0f) return _settings.slowMotionTimeScale;
            return 1f;
        }

        private void UpdateTimeScale(float udt)
        {
            if (!_ownsTimeScale) return;

            // 被別人改掉了（例如通關流程、其他系統）：放手
            if (Time.timeScale != _writtenTimeScale)
            {
                _ownsTimeScale = false;
                _hitstopLeft = _slowLeft = 0f;
                return;
            }

            // 頓幀常在物理步（OnTriggerEnter）開始，同一幀的 Update 不扣時間，避免少停一截
            if (Time.frameCount != _timeStartFrame)
            {
                if (_hitstopLeft > 0f) _hitstopLeft -= udt;
                else if (_slowLeft > 0f) _slowLeft -= udt;
            }

            float desired = DesiredTimeScale();
            ApplyTimeScale(desired);
            if (desired >= 1f) _ownsTimeScale = false;
        }

        private void ApplyTimeScale(float value)
        {
            Time.timeScale = value;
            _writtenTimeScale = value;
        }

        // ════════════════════════════════════════════════════════════
        //  手把震動
        // ════════════════════════════════════════════════════════════

        private void Rumble(HitImpactTier tier, float scale)
        {
            if (!RumbleEnabled || tier.rumbleDuration <= 0f) return;
            var pad = Gamepad.current;
            if (pad == null) return;

            float low = tier.rumbleLow * scale, high = tier.rumbleHigh * scale;
            if (low <= 0f && high <= 0f) return;

            if (_rumbleLeft > 0f && pad == _rumblePad)
            {
                low = Mathf.Max(low, _rumbleLow);
                high = Mathf.Max(high, _rumbleHigh);
            }
            else if (_rumblePad != null && _rumblePad != pad) StopRumble();

            _rumblePad = pad;
            _rumbleLow = low;
            _rumbleHigh = high;
            _rumbleLeft = Mathf.Max(_rumbleLeft, tier.rumbleDuration);
            pad.SetMotorSpeeds(low, high);
        }

        private void StopRumble()
        {
            if (_rumblePad != null)
            {
                try { _rumblePad.SetMotorSpeeds(0f, 0f); }
                catch (System.Exception) { /* 手把剛好拔掉 */ }
            }
            _rumblePad = null;
            _rumbleLeft = _rumbleLow = _rumbleHigh = 0f;
        }

        // ════════════════════════════════════════════════════════════
        //  受擊亮光，兩層一起作用：
        //   1. 等比例調亮：MaterialPropertyBlock 把每個材質槽的 _BaseColor 暫時乘亮（帶暖色），保留貼圖細節。
        //      沒有顏色型 _BaseColor 的材質（自訂 Shader Graph）跳過這一層。
        //   2. 暖色光暈：把模型的渲染層打開 HitFlashLayer，HitFlashRendererFeature 會再疊畫一層暖光
        //      （深色部位也看得到、輪廓更亮）。亮度 _HitGlow 一樣用 PropertyBlock 逐槽設定。
        //  不換材質，多材質槽的模型（身體＋臉）每一槽都作用；結束時清掉 PropertyBlock、還原渲染層。
        // ════════════════════════════════════════════════════════════

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int HitGlowId = Shader.PropertyToID("_HitGlow");
        private MaterialPropertyBlock _block;

        private void Flash(GameObject target, float duration, float intensity, float glow)
        {
            if (duration <= 0f || (intensity <= 0f && glow <= 0f) || target == null || _settings == null) return;

            if (!_flashes.TryGetValue(target, out var state))
            {
                state = new FlashState();
                _flashes[target] = state;
            }

            if (state.active)
            {
                // 亮光還沒退完又打中：拉回較強的那個，重新從亮開始退
                float rem = Remaining(state);
                state.peak = Mathf.Max(state.peak * rem, intensity);
                state.peakGlow = Mathf.Max(state.peakGlow * rem, glow);
                state.duration = Mathf.Max(duration, state.duration - state.elapsed);
                state.elapsed = 0f;
                ApplyFlash(state);
                return;
            }
            if (Time.unscaledTime - state.lastStart < _settings.flashMinInterval) return;

            _rendererBuffer.Clear();
            target.GetComponentsInChildren(false, _rendererBuffer);
            var renderers = new List<Renderer>();
            var counts = new List<int>();
            var colors = new List<Color?[]>();
            foreach (var r in _rendererBuffer)
            {
                if (r == null || !r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                var mats = r.sharedMaterials;
                Color?[] baseColors = null;
                for (int m = 0; m < mats.Length; m++)
                {
                    if (!HasColorProperty(mats[m], BaseColorId)) continue;
                    baseColors ??= new Color?[mats.Length];
                    baseColors[m] = mats[m].GetColor(BaseColorId);
                }
                baseColors ??= new Color?[mats.Length];   // 沒有顏色可調也要疊光暈
                renderers.Add(r);
                counts.Add(mats.Length);
                colors.Add(baseColors);
            }
            if (renderers.Count == 0) return;

            state.renderers = renderers.ToArray();
            state.materialCount = counts.ToArray();
            state.baseColors = colors.ToArray();
            state.originalLayers = new uint[state.renderers.Length];
            for (int i = 0; i < state.renderers.Length; i++)
            {
                state.originalLayers[i] = state.renderers[i].renderingLayerMask;
                state.renderers[i].renderingLayerMask |= HitFlashRendererFeature.HitFlashLayer;
            }
            state.duration = duration;
            state.elapsed = 0f;
            state.peak = intensity;
            state.peakGlow = glow;
            state.active = true;
            state.lastStart = Time.unscaledTime;
            ApplyFlash(state);
        }

        /// <summary>材質有沒有「顏色型」的 _BaseColor（Shader Graph 有時候同名但是貼圖，要排除）。</summary>
        private static bool HasColorProperty(Material mat, int id)
        {
            if (mat == null || !mat.HasProperty(id)) return false;
            var shader = mat.shader;
            int index = shader.FindPropertyIndex("_BaseColor");
            return index >= 0 && shader.GetPropertyType(index) == UnityEngine.Rendering.ShaderPropertyType.Color;
        }

        /// <summary>剩下的亮度比例：一打中最亮，之後平滑退掉。</summary>
        private static float Remaining(FlashState state)
        {
            if (state.duration <= 0f) return 0f;
            float k = 1f - Mathf.Clamp01(state.elapsed / state.duration);
            return k * k;
        }

        private void ApplyFlash(FlashState state)
        {
            _block ??= new MaterialPropertyBlock();
            float rem = Remaining(state);
            float amount = state.peak * rem;
            float glow = state.peakGlow * rem;
            Color tint = _settings.flashTint;
            // 每個色版各自乘亮：暖色 = 紅多亮一點、藍少亮一點
            var mul = new Color(1f + amount * tint.r, 1f + amount * tint.g, 1f + amount * tint.b, 1f);

            for (int i = 0; i < state.renderers.Length; i++)
            {
                var r = state.renderers[i];
                if (r == null) continue;
                var bases = state.baseColors[i];
                for (int m = 0; m < state.materialCount[i]; m++)
                {
                    _block.Clear();
                    if (bases[m].HasValue)
                    {
                        var c = bases[m].Value;
                        _block.SetColor(BaseColorId, new Color(c.r * mul.r, c.g * mul.g, c.b * mul.b, c.a));
                    }
                    _block.SetFloat(HitGlowId, glow);
                    r.SetPropertyBlock(_block, m);
                }
            }
        }

        private void UpdateFlashes(float udt)
        {
            if (_flashes.Count == 0) return;
            _flashKeys.Clear();
            _flashKeys.AddRange(_flashes.Keys);
            foreach (var key in _flashKeys)
            {
                var state = _flashes[key];
                if (key == null) { EndFlash(state); _flashes.Remove(key); continue; }   // 目標已被銷毀
                if (!state.active) continue;
                state.elapsed += udt;
                if (state.elapsed >= state.duration) EndFlash(state);
                else ApplyFlash(state);
            }
        }

        private static void EndFlash(FlashState state)
        {
            if (!state.active) return;
            state.active = false;
            if (state.renderers == null) return;
            for (int i = 0; i < state.renderers.Length; i++)
            {
                var r = state.renderers[i];
                if (r == null) continue;
                for (int m = 0; m < state.materialCount[i]; m++)
                    r.SetPropertyBlock(null, m);   // 清掉 → 回到材質原本的樣子
                r.renderingLayerMask = state.originalLayers[i];
            }
        }

        private void EndAllFlashes()
        {
            foreach (var state in _flashes.Values) EndFlash(state);
        }
    }
}
