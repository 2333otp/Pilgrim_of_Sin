using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 玩家攻擊碰撞體。
    /// 掛在 Player 的子物件上，由 PlayerCombat 啟用/停用。
    /// 同一次攻擊每個目標只打一次。
    /// </summary>
    public class PlayerAttackHitbox : MonoBehaviour
    {
        private float _damage;
        private readonly HashSet<IDamageable> _hitTargets = new HashSet<IDamageable>();

        /// <summary>啟用前設定傷害值並清空命中記錄。</summary>
        public void Activate(float damage)
        {
            _damage = damage;
            _hitTargets.Clear();
            gameObject.SetActive(true);
        }

        public void Deactivate() => gameObject.SetActive(false);

        private void OnTriggerEnter(Collider other)
        {
            var target = other.GetComponentInParent<IDamageable>();
            if (target == null) return;
            if (_hitTargets.Contains(target)) return;

            _hitTargets.Add(target);
            target.TakeDamage(_damage);
        }

        private void Awake() => gameObject.SetActive(false); // 預設關閉
    }
}
