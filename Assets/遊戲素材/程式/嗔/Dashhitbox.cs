using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 衝撞碰撞體，掛在 WrathBoss 的子物件上。
    /// 由 DashState 啟用/停用。
    /// </summary>
    public class DashHitbox : MonoBehaviour
    {
        [SerializeField] private float _damage = 400f; // Inspector 可調

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            var player = other.GetComponent<PlayerController>();
            player?.TakeDamage(_damage);
            Debug.Log($"[DashHitbox] 衝撞玩家，傷害 {_damage}");
        }
    }
}
