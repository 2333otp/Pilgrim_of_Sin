using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 惡區：初始覆蓋整個地圖，隨 Boss HP 線性縮小。
    /// 玩家在惡區內每隔固定秒數受傷一次（避免每幀觸發 Damaged 卡住移動）。
    /// </summary>
    public class EvilZone : MonoBehaviour
    {
        [Header("Evil Zone Settings")]
        [SerializeField] private float _maxRadius = 30f;
        [SerializeField] private float _damagePerSec = 200f;
        [SerializeField] private float _damageInterval = 1.0f; // 每幾秒打一次，Inspector 可調

        private bool _playerInside = false;
        private PlayerController _player;
        private float _damageTimer = 0f;

        private void Update()
        {
            if (!_playerInside || _player == null) return;

            _damageTimer += Time.deltaTime;
            if (_damageTimer >= _damageInterval)
            {
                _damageTimer = 0f;
                _player.TakeDamage(_damagePerSec * _damageInterval);
            }
        }

        /// <summary>依 HP 百分比設定惡區大小（1.0=最大，0.0=消失）。</summary>
        public void SetSizeByHpPercent(float hpPercent)
        {
            float radius = _maxRadius * Mathf.Clamp01(hpPercent);
            float diameter = radius * 2f;
            transform.localScale = new Vector3(diameter, 1f, diameter);
            Debug.Log($"[EvilZone] 惡區半徑 = {radius:F1}（HP {hpPercent:P0}）");
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            _player = other.GetComponent<PlayerController>();
            _playerInside = true;
            _damageTimer = 0f; // 進入惡區時重置計時器，不要一進去就立刻扣血
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            _playerInside = false;
            _player = null;
        }
    }
}