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

        /// <summary>
        /// 這一次揮擊是否已經打到 Boss（IBossHealth）。給天秤判斷用：同一次揮擊如果已經打到 Boss，
        /// 就不該同時算成「攻擊天秤」，否則站在天秤旁邊打 Boss 會誤觸天秤重製。
        /// </summary>
        public bool HitBossThisSwing { get; private set; }

        /// <summary>每次揮擊（Activate）加 1。讓「一次揮擊碰到多個碰撞體」的對象（例如天秤）能辨認同一擊、只處理一次。</summary>
        public int SwingId { get; private set; }

        /// <summary>啟用前設定傷害值並清空命中記錄。</summary>
        public void Activate(float damage)
        {
            _damage = damage;
            _hitTargets.Clear();
            HitBossThisSwing = false;
            SwingId++;
            gameObject.SetActive(true);
        }

        public void Deactivate() => gameObject.SetActive(false);

        private void OnTriggerEnter(Collider other)
        {
            var target = other.GetComponentInParent<IDamageable>();
            if (target == null) return;
            if (_hitTargets.Contains(target)) return;

            _hitTargets.Add(target);
            if (target is IBossHealth) HitBossThisSwing = true;
            target.TakeDamage(_damage);
        }

        private void Awake() => gameObject.SetActive(false); // 預設關閉
    }
}
