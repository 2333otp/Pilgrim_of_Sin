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
        [SerializeField] private float _maxWeight = 50f;
        [SerializeField] private Transform _leftSide;
        [SerializeField] private Transform _rightSide;
        [SerializeField] private GameObject _moneybagPrefab;

        public event Action<float> OnWeightChanged;

        private float _leftWeight;
        private float _rightWeight;
        private readonly List<MoneybagObject> _activeMoneybags = new List<MoneybagObject>();

        public float WeightDifference => _rightWeight - _leftWeight;

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

        public void RemoveMoneybag(MoneybagObject bag)
        {
            if (!_activeMoneybags.Remove(bag)) return;
            AddWeight(Side.Right, -bag.Weight);
            Destroy(bag.gameObject);
        }

        public void AddPlayerWeight(float weight) => AddWeight(Side.Left, weight);
        public void RemovePlayerWeight(float weight) => AddWeight(Side.Left, -weight);

        public void ResetScale()
        {
            foreach (var bag in _activeMoneybags)
                if (bag) Destroy(bag.gameObject);
            _activeMoneybags.Clear();

            _leftWeight = 0f;
            _rightWeight = 0f;
            NotifyWeightChanged();
        }

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
}