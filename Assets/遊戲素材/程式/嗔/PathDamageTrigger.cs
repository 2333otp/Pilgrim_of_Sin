using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 嗔 Boss 衝刺路徑殘留傷害。
    /// 由 WrathBossController.MoveDash() 動態生成，
    /// 自動在 _lifetime 秒後銷毀。
    /// 同一個 Trigger 只打玩家一次（OnTriggerEnter）。
    /// </summary>
    public class PathDamageTrigger : MonoBehaviour
    {
        [SerializeField] private float _damage = 400f;   // Inspector 可調
        [SerializeField] private float _lifetime = 5f;   // Inspector 可調

        private bool _hasHit = false;

        private void OnEnable()
        {
            _hasHit = false;
            Destroy(gameObject, _lifetime);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_hasHit) return;
            if (!other.CompareTag("Player")) return;
            _hasHit = true;
            var player = other.GetComponent<PlayerController>();
            player?.TakeDamage(_damage);
            Debug.Log($"[PathDamage] 路徑傷害命中玩家，傷害 {_damage}");
        }
    }
}
