using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 錢袋生成器。
    /// 負責：
    ///   - 每次循環在地板隨機位置生成 N 個錢袋
    ///   - 保證生成的錢袋中至少有一種組合，總重落在平衡區間（預設 25~30）
    ///   - 循環結束時清除所有錢袋並重新生成
    /// 掛載位置：GreedBoss 場景中任意 GameObject（建議掛在 GreedBossController 同一物件上）
    /// </summary>
    public class MoneybagSpawner : MonoBehaviour
    {
        // ── References ────────────────────────────────────────────────
        [Header("References")]
        [SerializeField] private GameObject _moneybagPrefab;    // 錢袋 Prefab
        [SerializeField] private ScaleObject _scale;             // 天秤物件
        [SerializeField] private Transform _scaleRightSide;    // 天秤右側位置
        [SerializeField] private GameObject _interactPromptUI;  // 右下角提示 UI（場景中唯一一個）

        // ── 生成範圍 ──────────────────────────────────────────────────
        [Header("Spawn Area")]
        [SerializeField] private float _spawnAreaMinX = -10f;
        [SerializeField] private float _spawnAreaMaxX = 10f;
        [SerializeField] private float _spawnAreaMinZ = -10f;
        [SerializeField] private float _spawnAreaMaxZ = 10f;
        [SerializeField] private float _spawnY = 0.5f;  // 地板高度（錢袋中心點）

        // ── 錢袋數量 ──────────────────────────────────────────────────
        [Header("Moneybag Count")]
        [SerializeField] private int _countMin = 3;
        [SerializeField] private int _countMax = 6;

        // ── 平衡區間 ──────────────────────────────────────────────────
        [Header("Balance Range")]
        [SerializeField] private float _balanceMin = 25f;
        [SerializeField] private float _balanceMax = 30f;

        // ── 單顆重量範圍 ──────────────────────────────────────────────
        [Header("Moneybag Weight")]
        [SerializeField] private float _weightMin = 3f;
        [SerializeField] private float _weightMax = 15f;

        // ── 驗證上限 ──────────────────────────────────────────────────
        [Header("Validation")]
        [SerializeField] private int _maxRetries = 100;

        // ── 內部 ──────────────────────────────────────────────────────
        private readonly List<MoneybagObject> _activeBags = new List<MoneybagObject>();

        // ════════════════════════════════════════════════════════════
        //  公開 API（由 GreedBossController 呼叫）
        // ════════════════════════════════════════════════════════════

        /// <summary>生成本循環的所有錢袋。</summary>
        public void SpawnCycle()
        {
            ClearAll();

            List<float> weights = GenerateValidWeights();
            if (weights == null)
            {
                Debug.LogWarning("[MoneybagSpawner] 無法在重試上限內生成合法錢袋組合，使用備用方案。");
                weights = FallbackWeights();
            }

            foreach (float w in weights)
            {
                Vector3 pos = RandomSpawnPosition();
                SpawnOne(w, pos);
            }

            Debug.Log($"[MoneybagSpawner] 本循環生成 {weights.Count} 個錢袋。");
        }

        /// <summary>循環結束時清除所有錢袋（由 GreedBossController.ResetScale 呼叫）。</summary>
        public void ClearAll()
        {
            foreach (var bag in _activeBags)
                if (bag) Destroy(bag.gameObject);
            _activeBags.Clear();
        }

        // ════════════════════════════════════════════════════════════
        //  生成邏輯
        // ════════════════════════════════════════════════════════════

        private void SpawnOne(float weight, Vector3 position)
        {
            if (_moneybagPrefab == null) return;

            var go = Instantiate(_moneybagPrefab, position, Quaternion.identity);
            var bag = go.GetComponent<MoneybagObject>();
            if (bag == null) { Destroy(go); return; }

            bag.Init(weight, _scale, _scaleRightSide, _interactPromptUI);
            _activeBags.Add(bag);
        }

        private Vector3 RandomSpawnPosition()
        {
            float x = Random.Range(_spawnAreaMinX, _spawnAreaMaxX);
            float z = Random.Range(_spawnAreaMinZ, _spawnAreaMaxZ);
            return new Vector3(x, _spawnY, z);
        }

        // ════════════════════════════════════════════════════════════
        //  驗證邏輯：保證至少一種子集合總重落在平衡區間
        // ════════════════════════════════════════════════════════════

        private List<float> GenerateValidWeights()
        {
            for (int attempt = 0; attempt < _maxRetries; attempt++)
            {
                int count = Random.Range(_countMin, _countMax + 1);
                var weights = new List<float>();

                for (int i = 0; i < count; i++)
                    weights.Add(Random.Range(_weightMin, _weightMax));

                if (HasValidCombination(weights))
                    return weights;
            }
            return null;
        }

        /// <summary>
        /// 窮舉所有子集合，檢查是否至少有一個子集合的總重落在平衡區間。
        /// N 最大 6，子集合最多 2^6 = 64 個，效能沒問題。
        /// </summary>
        private bool HasValidCombination(List<float> weights)
        {
            int n = weights.Count;
            int total = 1 << n; // 2^n

            for (int mask = 1; mask < total; mask++)
            {
                float sum = 0f;
                for (int i = 0; i < n; i++)
                    if ((mask & (1 << i)) != 0)
                        sum += weights[i];

                if (sum >= _balanceMin && sum <= _balanceMax)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 備用方案：直接生成一組保證可以平衡的錢袋。
        /// 當重試超過上限時使用。
        /// </summary>
        private List<float> FallbackWeights()
        {
            float target = (_balanceMin + _balanceMax) / 2f;
            float w1 = target * 0.4f;
            float w2 = target * 0.35f;
            float w3 = target - w1 - w2;
            return new List<float> { w1, w2, w3 };
        }
    }
}