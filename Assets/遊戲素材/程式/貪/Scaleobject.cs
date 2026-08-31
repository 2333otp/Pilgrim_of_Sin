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

        // ── Break 動畫 ────────────────────────────────────────────────
        [Header("Break Animation")]
        [SerializeField] private float _breakDuration = 2.4f; // Break clip 長度，播完視為「踢翻完成」

        // ── Animator 參數 ────────────────────────────────────────────
        private static readonly int TiltHash    = Animator.StringToHash("Tilt");
        private static readonly int DoBreakHash = Animator.StringToHash("DoBreak");

        private const int TiltCenter = 0;
        private const int TiltScaleLeft  = 1; // CtoL
        private const int TiltScaleRight = 2; // CtoR

        // ── 內部 ──────────────────────────────────────────────────────
        private float _rightWeight;
        private int _currentTilt = -1;

        // ── 事件 ──────────────────────────────────────────────────────
        /// <summary>右側重量變化時觸發，傳出當前右側總重。</summary>
        public event Action<float> OnWeightChanged;

        /// <summary>Break 動畫播完時觸發（供 GreedBossController 收尾用）。</summary>
        public event Action OnBreakComplete;

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
        //  · 超重狀態：觸發循環重製（之後補受擊動畫）
        //  · 其他狀態：打落天秤上所有錢袋，並重置攻擊窗口計時器
        // ════════════════════════════════════════════════════════════

        private void OnTriggerEnter(Collider other)
        {
            if (other.GetComponent<PlayerAttackHitbox>() == null) return;

            // 超重時攻擊天秤 → 循環重製（唯一能結束超重狀態的方式）
            if (_bossController != null && _bossController.CurrentPhase == ScalePhase.MoneyBagHeavy)
            {
                _bossController.OnScaleHitWhileOverweight();
                return;
            }

            var bags = FindObjectsByType<MoneybagObject>(FindObjectsSortMode.None);
            foreach (var bag in bags)
                bag.TakeDamage(0f);

            _bossController?.ResetBalanceWindow();
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
