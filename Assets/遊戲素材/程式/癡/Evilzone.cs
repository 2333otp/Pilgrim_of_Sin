using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 惡區：初始覆蓋整個地圖，隨 Boss HP 線性縮小。
    /// 玩家在惡區內每秒持續受傷。
    /// </summary>
    public class EvilZone : MonoBehaviour
    {
        [Header("Evil Zone Settings")]
        [SerializeField] private float _maxRadius = 30f;  // 初始最大半徑，Inspector 可調
        [SerializeField] private float _damagePerSec = 200f; // 在惡區每秒受到的傷害，Inspector 可調

        private bool _playerInside = false;
        private PlayerController _player;

        private void Update()
        {
            if (_playerInside && _player != null)
                _player.TakeDamage(_damagePerSec * Time.deltaTime);
        }

        /// <summary>依 HP 百分比設定惡區大小（1.0=最大，0.0=消失）。</summary>
        public void SetSizeByHpPercent(float hpPercent)
        {
            float radius = _maxRadius * Mathf.Clamp01(hpPercent);
            // Sphere 預設直徑1，所以 localScale = 直徑 = radius * 2
            float diameter = radius * 2f;
            transform.localScale = new Vector3(diameter, 1f, diameter);
            Debug.Log($"[EvilZone] 惡區半徑 = {radius:F1}（HP {hpPercent:P0}）");
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            _player = other.GetComponent<PlayerController>();
            _playerInside = true;
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            _playerInside = false;
            _player = null;
        }
    }
}