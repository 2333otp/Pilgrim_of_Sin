using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 爆炸碰撞體，掛在 WrathBoss 的子物件上。
    /// 由 Animation Event 啟用/停用，同一次爆炸只打一次。
    /// </summary>
    public class ExplosionHitbox : MonoBehaviour
    {
        [SerializeField] private float _damage = 600f; // Inspector 可調

        private bool _hasHit = false;

        private void OnEnable() => _hasHit = false;

        private void OnTriggerEnter(Collider other)
        {
            if (_hasHit) return;
            if (!other.CompareTag("Player")) return;
            _hasHit = true;
            var player = other.GetComponent<PlayerController>();
            player?.TakeDamage(_damage);
            Debug.Log($"[ExplosionHitbox] 爆炸命中玩家，傷害 {_damage}");
        }
    }
}
