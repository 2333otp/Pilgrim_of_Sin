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
            Debug.Log($"[AttackHitbox] 啟動，傷害={damage}");
        }

        public void Deactivate() => gameObject.SetActive(false);

        private void OnEnable()
        {
            Debug.Log("[AttackHitbox] 碰撞體 OnEnable");
        }

        private void OnTriggerEnter(Collider other)
        {
            var target = other.GetComponentInParent<IDamageable>();
            Debug.Log($"[AttackHitbox] 碰到 {other.name}，IDamageable={(target != null ? target.GetType().Name : "null")}");
            if (target == null) return;
            if (_hitTargets.Contains(target)) return;

            _hitTargets.Add(target);
            target.TakeDamage(_damage);
            Debug.Log($"[AttackHitbox] 命中 {other.name}，傷害 {_damage}");
        }

        private void Awake() => gameObject.SetActive(false); // 預設關閉
    }
}
