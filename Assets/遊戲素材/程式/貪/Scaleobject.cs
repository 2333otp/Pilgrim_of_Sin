using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 天秤場景物件（Libra 模型版）。
    /// 負責：
    ///   - 追蹤右側錢袋總重量
    ///   - 重量變化時通知 GreedBossController 判斷天秤狀態
    ///   - 依平衡結果驅動 Libra Animator（離散三狀態：置中 / 天秤左傾 / 天秤右傾）
    ///   - 天秤被踢翻時播 Break 動畫，並在動畫結束後通知外部
    /// 注意：左側雕像不需要數值，平衡判斷純依據右側總重的區間。
    /// 座標約定：Animator 的 Tilt 參數用「天秤自身視角」的左右（跟 clip 名 CtoL/CtoR 一致），
    ///           不是玩家看過去的方向。若視覺方向相反，翻 _statueSideIsScaleRight。
    /// </summary>
    public class ScaleObject : MonoBehaviour
    {
        // ── References ────────────────────────────────────────────────
        [Header("References")]
        [SerializeField] private Animator _libraAnimator;              // Libra 模型上的 Animator（LibraScale.controller）
        [SerializeField] private GreedBossController _bossController;   // 拖入 GreedBossController
        [SerializeField] private GreedScaleConfig _config;              // Assets/遊戲素材/SO 底下的設定，企劃可直接調整

        // ── 傾斜方向對應 ──────────────────────────────────────────────
        [Header("Tilt Mapping")]
        [Tooltip("雕像（固定配重）那一側是否落在天秤自身的『右』邊。CtoR 動畫會讓天秤右側下沉。\n" +
                 "若進 play 後發現傾斜方向跟預期相反，把這個打勾狀態反過來即可。")]
        [SerializeField] private bool _statueSideIsScaleRight = true;

        // ── 平衡區間 ──────────────────────────────────────────────────
        [Header("Balance Range")]
        [SerializeField] private float _balanceMin = 25f; // 平衡下限
        [SerializeField] private float _balanceMax = 40f; // 平衡上限（視窗寬15，較易達成）
        [SerializeField] private float _maxWeight = 50f; // 右側最大重量上限（Clamp 用）

        // 動畫、音效、特效都在 GreedScaleConfig（_config）裡設定。
        // 下面兩個時長只在 SO 找不到對應 state 時當備用；有 SO 時一律用動畫實際長度。
        [Header("備用時長（SO 沒設定時才用）")]
        [SerializeField] private float _breakDuration = 2.4f;    // Break 播完視為「踢翻完成」
        [SerializeField] private float _hitResetDuration = 1.3f; // Attacked + MoneyBagUpdate 播完視為「重製完成」

        private const string StateBreak = "Break", StateAttacked = "Attacked", StateMoneyBagUpdate = "MoneyBagUpdate";

        // ── Animator 參數 ────────────────────────────────────────────
        private static readonly int TiltHash       = Animator.StringToHash("Tilt");
        private static readonly int DoBreakHash    = Animator.StringToHash("DoBreak");
        private static readonly int DoHitResetHash = Animator.StringToHash("DoHitReset");

        private const int TiltCenter = 0;
        private const int TiltScaleLeft  = 1; // CtoL
        private const int TiltScaleRight = 2; // CtoR

        // ── 內部 ──────────────────────────────────────────────────────
        private float _rightWeight;
        private int _currentTilt = -1;
        private int _lastHitSfxSwingId = -1;
        private int _hitCount; // 累積攻擊次數，達到 _config.HitsRequiredToTrigger 才觸發受擊+重製

        private GreedFxPlayer _fx;
        private GreedFxPlayer Fx => _fx ??= new GreedFxPlayer(transform, () => _config != null ? _config.mixerGroup : null);
        private Dictionary<int, ScaleAnimEntry> _entryByHash;
        private int _fxStateHash;   // 目前正在播特效的 state（換 state 時重新開始）

        // ── 事件 ──────────────────────────────────────────────────────
        /// <summary>右側重量變化時觸發，傳出當前右側總重。</summary>
        public event Action<float> OnWeightChanged;

        /// <summary>Break（踢翻）動畫開始時觸發（供雕像 ScaleStatue 準備被甩出去）。</summary>
        public event Action OnBreakStarted;

        /// <summary>受擊+重製動畫開始時觸發。</summary>
        public event Action OnHitResetStarted;

        /// <summary>Break 動畫播完時觸發（供 GreedBossController 收尾用）。</summary>
        public event Action OnBreakComplete;

        /// <summary>受擊+重製動畫播完時觸發（供 GreedBossController 收尾用）。</summary>
        public event Action OnHitResetComplete;

        // ── 公開屬性 ──────────────────────────────────────────────────
        public float RightWeight => _rightWeight;

        /// <summary>玩家視角看到的天秤傾斜方向（給 HUD 圖示用，跟 3D 天秤同一套來源）。</summary>
        public enum ViewerTilt { Balanced, TiltLeft, TiltRight }

        public ViewerTilt CurrentViewerTilt
        {
            get
            {
                if (_currentTilt < 0 || _currentTilt == TiltCenter) return ViewerTilt.Balanced;
                // 實測：天秤自身右側下沉(CtoR) = 遊戲鏡頭下玩家看到「右傾」（右碗下沉）
                return _currentTilt == TiltScaleRight ? ViewerTilt.TiltRight : ViewerTilt.TiltLeft;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  Unity 生命週期
        // ════════════════════════════════════════════════════════════

        private void Awake()
        {
            ApplyAnimationOverrides();
        }

        private void Start()
        {
            // 初始 0 重量 → 雕像重 → 進場停在對應側（跟 Animator Entry 的 ToRight 一致）
            ApplyTiltFromWeight();
        }

        private void Update()
        {
            UpdateStateFx();
        }

        /// <summary>
        /// SO 裡有換動畫的 state，用 AnimatorOverrideController 把原本的 Clip 換掉。
        /// 全部維持原樣就完全不碰 Animator。必須在設定 Animator 參數之前做（換 Controller 會重置參數）。
        /// </summary>
        private void ApplyAnimationOverrides()
        {
            if (_config == null || _libraAnimator == null || _libraAnimator.runtimeAnimatorController == null) return;

            var swaps = new Dictionary<AnimationClip, AnimationClip>();
            foreach (var s in _config.states)
                if (s != null && s.clip != null && s.baseClip != null && s.clip != s.baseClip)
                    swaps[s.baseClip] = s.clip;
            if (swaps.Count == 0) return;

            var oc = new AnimatorOverrideController(_libraAnimator.runtimeAnimatorController);
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            oc.GetOverrides(pairs);
            for (int i = 0; i < pairs.Count; i++)
                if (swaps.TryGetValue(pairs[i].Key, out var rep))
                    pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(pairs[i].Key, rep);
            oc.ApplyOverrides(pairs);
            _libraAnimator.runtimeAnimatorController = oc;
        }

        /// <summary>
        /// 每幀看 Animator 正在播哪個 state（轉換中以「要轉過去的」為準），
        /// 換 state 時開始播那個 state 的音效 / 特效，並依播放進度觸發各時間點。
        /// </summary>
        private void UpdateStateFx()
        {
            if (_config == null || _libraAnimator == null || !_libraAnimator.isActiveAndEnabled) return;

            if (_entryByHash == null)
            {
                _entryByHash = new Dictionary<int, ScaleAnimEntry>();
                foreach (var s in _config.states)
                    if (s != null && !string.IsNullOrEmpty(s.stateName))
                        _entryByHash[Animator.StringToHash(s.stateName)] = s;
            }

            var info = _libraAnimator.IsInTransition(0)
                ? _libraAnimator.GetNextAnimatorStateInfo(0)
                : _libraAnimator.GetCurrentAnimatorStateInfo(0);

            if (info.shortNameHash != _fxStateHash)
            {
                _fxStateHash = info.shortNameHash;
                if (_entryByHash.TryGetValue(_fxStateHash, out var entry)) Fx.Begin(entry.fx);
                else Fx.Stop();
            }
            Fx.Tick(Mathf.Min(1f, info.normalizedTime));
        }

        // ════════════════════════════════════════════════════════════
        //  攻擊偵測 — 任何帶 PlayerAttackHitbox 的碰撞進入時
        //  不論當下相位（雕像重/平衡/錢袋重），累積攻擊次數達到
        //  _config.HitsRequiredToTrigger 就播受擊+重製動畫（見 PlayHitReset）。
        //  踢翻動畫播放中（Kicked）不計入，避免跟 Break 動畫互相打斷。
        // ════════════════════════════════════════════════════════════

        // 玩家的攻擊判定球變大後，站在天秤旁邊打 Boss 時，判定球常常同時碰到天秤的（很大的）觸發範圍。
        // 同一次揮擊如果有打到 Boss，就以 Boss 為優先，不算攻擊天秤。但判定球碰到天秤跟碰到 Boss 的
        // 先後順序不固定，所以先等一小段時間讓同一次揮擊的碰撞事件都到齊，再決定算不算。
        private const float ScaleHitConfirmDelay = 0.12f;

        private void OnTriggerEnter(Collider other)
        {
            // GetComponentInParent：主角的判定可能開在 AttackHitBox 底下的「判定槽」子物件上（出招表的形狀判定）
            var hitbox = other.GetComponentInParent<PlayerAttackHitbox>();
            if (hitbox == null) return;
            if (_bossController != null && _bossController.CurrentPhase == ScalePhase.Kicked) return;
            if (hitbox.HitBossThisSwing) return;

            StartCoroutine(ConfirmScaleHit(hitbox));
        }

        private System.Collections.IEnumerator ConfirmScaleHit(PlayerAttackHitbox hitbox)
        {
            yield return new WaitForSeconds(ScaleHitConfirmDelay);

            if (hitbox == null || hitbox.HitBossThisSwing) yield break;
            if (_bossController != null && _bossController.CurrentPhase == ScalePhase.Kicked) yield break;

            // 攻擊天秤的音效：天秤有 3 個碰撞體，一次揮擊會進來好幾次，用 SwingId 確保同一擊只響一次。
            if (hitbox.SwingId != _lastHitSfxSwingId)
            {
                _lastHitSfxSwingId = hitbox.SwingId;
                if (_config != null) Fx.FireAll(_config.hitFx);
            }

            _hitCount++;
            int required = _config != null ? _config.HitsRequiredToTrigger : 1;
            if (_hitCount < required) yield break;

            _hitCount = 0;
            _bossController?.OnScaleAttacked();
        }

        // ════════════════════════════════════════════════════════════
        //  公開 API（由 MoneybagObject 呼叫）
        // ════════════════════════════════════════════════════════════

        /// <summary>錢袋放上天秤右側時呼叫，增加右側重量。</summary>
        public void AddMoneybagWeight(float weight)
        {
            _rightWeight = Mathf.Clamp(_rightWeight + weight, 0f, _maxWeight);
            NotifyAndUpdateTilt();
        }

        /// <summary>錢袋從天秤打落時呼叫，減少右側重量。</summary>
        public void RemoveMoneybagWeight(float weight)
        {
            _rightWeight = Mathf.Clamp(_rightWeight - weight, 0f, _maxWeight);
            NotifyAndUpdateTilt();
        }

        /// <summary>
        /// 循環結束時重置天秤（由 GreedBossController.ResetScale 呼叫）。
        /// 清除重量並回到雕像重的傾斜狀態。
        /// </summary>
        public void ResetScale()
        {
            _rightWeight = 0f;
            NotifyAndUpdateTilt();
        }

        // ════════════════════════════════════════════════════════════
        //  Break（踢翻）
        // ════════════════════════════════════════════════════════════

        /// <summary>由 GreedBossController.PlayScaleBreak 呼叫：播 Break 動畫。</summary>
        public void PlayBreak()
        {
            // 翻倒音效 / 特效改由 SO 的 Break state 播（Animator 進入 Break 時自動觸發）
            OnBreakStarted?.Invoke();
            if (_libraAnimator != null) _libraAnimator.SetTrigger(DoBreakHash);
            StopAllCoroutines();
            StartCoroutine(BreakRoutine());
        }

        private IEnumerator BreakRoutine()
        {
            float d = _config != null ? _config.GetLength(StateBreak, _breakDuration) : _breakDuration;
            yield return new WaitForSeconds(d);
            OnBreakComplete?.Invoke();
        }

        // ════════════════════════════════════════════════════════════
        //  受擊 + 重製（超重時攻擊天秤）
        // ════════════════════════════════════════════════════════════

        /// <summary>由 GreedBossController.OnScaleAttacked 呼叫：播受擊+重製動畫。</summary>
        public void PlayHitReset()
        {
            OnHitResetStarted?.Invoke();
            if (_libraAnimator != null) _libraAnimator.SetTrigger(DoHitResetHash);
            StopAllCoroutines();
            StartCoroutine(HitResetRoutine());
        }

        private IEnumerator HitResetRoutine()
        {
            float d = _hitResetDuration;
            if (_config != null && _config.Find(StateAttacked) != null && _config.Find(StateMoneyBagUpdate) != null)
                d = _config.GetLength(StateAttacked, 0f) + _config.GetLength(StateMoneyBagUpdate, 0f);
            yield return new WaitForSeconds(d);
            OnHitResetComplete?.Invoke();
        }

        // ════════════════════════════════════════════════════════════
        //  天秤狀態判斷（供 GreedBossController 使用）
        // ════════════════════════════════════════════════════════════

        /// <summary>右側總重是否落在平衡區間。</summary>
        public bool IsBalanced()
            => _rightWeight >= _balanceMin && _rightWeight <= _balanceMax;

        /// <summary>右側過重（錢袋贏）。</summary>
        public bool IsRightHeavy()
            => _rightWeight > _balanceMax;

        /// <summary>右側過輕（雕像贏）。</summary>
        public bool IsLeftHeavy()
            => _rightWeight < _balanceMin;

        // ════════════════════════════════════════════════════════════
        //  內部：通知 + 驅動 Animator
        // ════════════════════════════════════════════════════════════

        private void NotifyAndUpdateTilt()
        {
            OnWeightChanged?.Invoke(_rightWeight);
            ApplyTiltFromWeight();
        }

        /// <summary>
        /// 依重量結果決定 Libra 的傾斜狀態並丟給 Animator。
        /// 平衡 → 置中；雕像重 → 傾向雕像側；錢袋重 → 傾向錢袋側。
        /// </summary>
        private void ApplyTiltFromWeight()
        {
            int desired;
            if (IsBalanced())
            {
                desired = TiltCenter;
            }
            else if (IsLeftHeavy()) // 雕像重（錢袋側過輕）
            {
                desired = _statueSideIsScaleRight ? TiltScaleRight : TiltScaleLeft;
            }
            else // 錢袋重
            {
                desired = _statueSideIsScaleRight ? TiltScaleLeft : TiltScaleRight;
            }

            if (desired == _currentTilt) return;
            _currentTilt = desired;
            if (_libraAnimator != null) _libraAnimator.SetInteger(TiltHash, desired);
        }
    }
}
