using System;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 天秤場景物件。
    /// 負責：
    ///   - 追蹤右側錢袋總重量
    ///   - 重量變化時通知 GreedBossController 判斷天秤狀態
    ///   - 天秤橫桿視覺傾斜（方案A：Z軸旋轉）
    ///   - 天秤被踢翻後重置
    /// 注意：左側雕像不需要數值，平衡判斷純依據右側總重的區間。
    /// </summary>
    public class ScaleObject : MonoBehaviour
    {
        // ── References ────────────────────────────────────────────────
        [Header("References")]
        [SerializeField] private Transform _beam;           // 天秤橫桿 Transform（負責 Z 軸旋轉）

        // ── 平衡區間 ──────────────────────────────────────────────────
        [Header("Balance Range")]
        [SerializeField] private float _balanceMin = 25f; // 平衡下限
        [SerializeField] private float _balanceMax = 30f; // 平衡上限
        [SerializeField] private float _maxWeight = 50f; // 右側最大重量上限（Clamp 用）

        // ── 視覺傾斜 ──────────────────────────────────────────────────
        [Header("Tilt Visual")]
        [SerializeField] private float _maxTiltAngle = 30f;    // 最大傾斜角度（度）
        [SerializeField] private float _tiltSpeed = 5f;     // 傾斜動畫速度

        // ── 內部 ──────────────────────────────────────────────────────
        private float _rightWeight;
        private float _targetTiltAngle;

        // ── 事件 ──────────────────────────────────────────────────────
        /// <summary>右側重量變化時觸發，傳出當前右側總重。</summary>
        public event Action<float> OnWeightChanged;

        // ── 公開屬性 ──────────────────────────────────────────────────
        public float RightWeight => _rightWeight;

        // ════════════════════════════════════════════════════════════
        //  Unity 生命週期
        // ════════════════════════════════════════════════════════════

        private void Update()
        {
            UpdateTiltVisual();
        }

        // ════════════════════════════════════════════════════════════
        //  公開 API（由 MoneybagObject 呼叫）
        // ════════════════════════════════════════════════════════════

        /// <summary>錢袋放上天秤右側時呼叫，增加右側重量。</summary>
        public void AddMoneybagWeight(float weight)
        {
            _rightWeight = Mathf.Clamp(_rightWeight + weight, 0f, _maxWeight);
            NotifyAndUpdateTilt();
            Debug.Log($"[Scale] 錢袋放上，右側總重 {_rightWeight:F1}");
        }

        /// <summary>錢袋從天秤打落時呼叫，減少右側重量。</summary>
        public void RemoveMoneybagWeight(float weight)
        {
            _rightWeight = Mathf.Clamp(_rightWeight - weight, 0f, _maxWeight);
            NotifyAndUpdateTilt();
            Debug.Log($"[Scale] 錢袋打落，右側總重 {_rightWeight:F1}");
        }

        /// <summary>
        /// 循環結束時重置天秤（由 GreedBossController.ResetScale 呼叫）。
        /// 清除重量並回正視覺。
        /// </summary>
        public void ResetScale()
        {
            _rightWeight = 0f;
            _targetTiltAngle = 0f;
            NotifyAndUpdateTilt();
            Debug.Log("[Scale] 天秤重置。");
        }

        // ════════════════════════════════════════════════════════════
        //  天秤狀態判斷（供 GreedBossController 使用）
        // ════════════════════════════════════════════════════════════

        /// <summary>右側總重是否落在平衡區間（25~30）。</summary>
        public bool IsBalanced()
            => _rightWeight >= _balanceMin && _rightWeight <= _balanceMax;

        /// <summary>右側過重（錢袋贏）。</summary>
        public bool IsRightHeavy()
            => _rightWeight > _balanceMax;

        /// <summary>右側過輕（雕像贏）。</summary>
        public bool IsLeftHeavy()
            => _rightWeight < _balanceMin;

        // ════════════════════════════════════════════════════════════
        //  內部：通知 + 計算傾斜目標角度
        // ════════════════════════════════════════════════════════════

        private void NotifyAndUpdateTilt()
        {
            OnWeightChanged?.Invoke(_rightWeight);
            CalculateTargetTilt();
        }

        /// <summary>
        /// 根據右側總重計算目標傾斜角度。
        /// 右側過重 → 右傾（負角度）
        /// 左側過重（雕像贏）→ 左傾（正角度）
        /// 平衡 → 水平（0度）
        /// </summary>
        private void CalculateTargetTilt()
        {
            if (IsBalanced())
            {
                _targetTiltAngle = 0f;
            }
            else if (IsRightHeavy())
            {
                // 右側越重，右傾越大
                float excess = (_rightWeight - _balanceMax) / (_maxWeight - _balanceMax);
                _targetTiltAngle = -_maxTiltAngle * Mathf.Clamp01(excess);
            }
            else
            {
                // 左側越重（右側越輕），左傾越大
                float deficit = (_balanceMin - _rightWeight) / _balanceMin;
                _targetTiltAngle = _maxTiltAngle * Mathf.Clamp01(deficit);
            }
        }

        // ════════════════════════════════════════════════════════════
        //  視覺更新（方案A：Z軸旋轉）
        // ════════════════════════════════════════════════════════════

        private void UpdateTiltVisual()
        {
            if (_beam == null) return;

            float currentZ = _beam.localEulerAngles.z;
            // 將 0~360 換算回 -180~180 方便插值
            if (currentZ > 180f) currentZ -= 360f;

            float newZ = Mathf.Lerp(currentZ, _targetTiltAngle, Time.deltaTime * _tiltSpeed);
            _beam.localEulerAngles = new Vector3(
                _beam.localEulerAngles.x,
                _beam.localEulerAngles.y,
                newZ);
        }
    }
}