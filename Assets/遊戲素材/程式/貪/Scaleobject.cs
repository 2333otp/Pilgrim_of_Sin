using System;
using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 天秤場景物件。
    /// 負責：
    ///   - 追蹤左右兩側重量
    ///   - 生成/移除錢袋
    ///   - 重量變化時通知 GreedBossController
    ///   - 天秤被踢翻後重置
    /// </summary>
    public class ScaleObject : MonoBehaviour
    {
        [Header("Scale Settings")]
        [SerializeField] private float _maxWeight = 50f;  // 天秤最大重量
        [SerializeField] private Transform _leftSide;         // 左側秤盤（玩家側）
        [SerializeField] private Transform _rightSide;        // 右側秤盤（錢袋側）
        [SerializeField] private GameObject _moneybagPrefab;  // 錢袋預製體

        /// <summary>重量差變化時通知：正數=右重，負數=左重（玩家側重）。</summary>
        public event Action<float> OnWeightChanged;

        private float _leftWeight;
        private float _rightWeight;
        private readonly List<MoneybagObject> _activeMoneybags = new List<MoneybagObject>();

        public float WeightDifference => _rightWeight - _leftWeight;

        // ── 公開介面 ─────────────────────────────────────────────────

        /// <summary>生成一個錢袋掛在右側秤盤。</summary>
        public void SpawnMoneybag(float weight)
        {
            if (_moneybagPrefab == null || _rightSide == null) return;

            var go = Instantiate(_moneybagPrefab, _rightSide);
            var bag = go.GetComponent<MoneybagObject>();
            if (bag == null) { Destroy(go); return; }

            bag.Init(weight, this);
            _activeMoneybags.Add(bag);
            AddWeight(Side.Right, weight);
        }

        /// <summary>錢袋被拾取或擊落時呼叫。</summary>
        public void RemoveMoneybag(MoneybagObject bag)
        {
            if (!_activeMoneybags.Remove(bag)) return;
            AddWeight(Side.Right, -bag.Weight);
            Destroy(bag.gameObject);
        }

        /// <summary>玩家站上左側秤盤時增加重量（碰撞體觸發）。</summary>
        public void AddPlayerWeight(float weight) => AddWeight(Side.Left, weight);
        public void RemovePlayerWeight(float weight) => AddWeight(Side.Left, -weight);

        /// <summary>踢翻後重置。</summary>
        public void ResetScale()
        {
            foreach (var bag in _activeMoneybags)
                if (bag) Destroy(bag.gameObject);
            _activeMoneybags.Clear();

            _leftWeight = 0f;
            _rightWeight = 0f;
            NotifyWeightChanged();
        }

        // ── 私有 ─────────────────────────────────────────────────────

        private enum Side { Left, Right }

        private void AddWeight(Side side, float delta)
        {
            if (side == Side.Left)
                _leftWeight = Mathf.Clamp(_leftWeight + delta, 0f, _maxWeight);
            else
                _rightWeight = Mathf.Clamp(_rightWeight + delta, 0f, _maxWeight);

            NotifyWeightChanged();
        }

        private void NotifyWeightChanged()
        {
            OnWeightChanged?.Invoke(WeightDifference);
            Debug.Log($"[Scale] 左={_leftWeight:F1} 右={_rightWeight:F1} 差={WeightDifference:F1}");
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  MoneybagObject  錢袋個體
    // ════════════════════════════════════════════════════════════════
    public class MoneybagObject : MonoBehaviour
    {
        public float Weight { get; private set; }
        private ScaleObject _scale;

        public void Init(float weight, ScaleObject scale)
        {
            Weight = weight;
            _scale = scale;
        }

        /// <summary>玩家靠近拾取（OnTriggerEnter 呼叫）。</summary>
        public void PickUp() => _scale?.RemoveMoneybag(this);

        /// <summary>被攻擊擊落（由攻擊系統呼叫）。</summary>
        public void KnockOff() => _scale?.RemoveMoneybag(this);
    }

    // ════════════════════════════════════════════════════════════════
    //  ScaleHitbox  天秤碰撞體（踢翻傷害）
    //  掛在天秤碰撞體物件上，由 Animation Event 啟用/停用
    // ════════════════════════════════════════════════════════════════
    public class ScaleHitbox : MonoBehaviour
    {
        [SerializeField] private float _damage = 700f; // Inspector 可調

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            var player = other.GetComponent<PlayerController>();
            player?.TakeDamage(_damage);
        }
    }
}