using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 天秤踢翻碰撞體，掛在 ScaleHitbox 子物件上。
    /// 由 Animation Event 啟用/停用。
    /// </summary>
    public class ScaleHitbox : MonoBehaviour
    {
        [SerializeField] private float _damage = 700f; // Inspector 可調（GreedBossController 會用 _scaleKickDamage 覆寫）

        /// <summary>由 GreedBossController 在踢翻時推入傷害值，統一數值來源。</summary>
        public void SetDamage(float d) => _damage = d;

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            var player = other.GetComponent<PlayerController>();
            player?.TakeDamage(_damage);
        }
    }
}