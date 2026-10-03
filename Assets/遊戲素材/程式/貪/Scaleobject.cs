using System;
using System.Collections;
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

        // ── 音效（把音檔拖進這兩個 SO 資產即可，不用改程式）─────────────
        [Header("Sound Effects")]
        [Tooltip("攻擊天秤時（一次揮擊打到天秤只播一次）")]
        [SerializeField] private SoundEffectData _hitSfx;
        [Tooltip("天秤翻倒時（Boss 踢翻天秤、播 Break 動畫的瞬間；撞擊聲要對齊動畫可在 SO 設「延遲」）")]
        [SerializeField] private SoundEffectData _breakSfx;

        // ── Break 動畫 ────────────────────────────────────────────────
        [Header("Break Animation")]
        [SerializeField] private float _breakDuration = 2.4f; // Break clip 長度，播完視為「踢翻完成」

        // ── 受擊 + 重製動畫（超重時攻擊天秤觸發）────────────────────────
        [Header("Hit + Reset Animation")]
        [SerializeField] private float _hitResetDuration = 1.3f; // Attacked(0.25s) + MoneyBagUpdate(1.04s) 播完視為「重製完成」

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

        private void Start()
        {
            // 初始 0 重量 → 雕像重 → 進場停在對應側（跟 Animator Entry 的 ToRight 一致）
            ApplyTiltFromWeight();
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
            var hitbox = other.GetComponent<PlayerAttackHitbox>();
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
                _hitSfx?.Play();
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
            _breakSfx?.Play();
            OnBreakStarted?.Invoke();
            if (_libraAnimator != null) _libraAnimator.SetTrigger(DoBreakHash);
            StopAllCoroutines();
            StartCoroutine(BreakRoutine());
        }

        private IEnumerator BreakRoutine()
        {
            yield return new WaitForSeconds(_breakDuration);
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
            yield return new WaitForSeconds(_hitResetDuration);
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
