using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 善區：玩家進入後免疫惡區持續傷害。
    /// 使用獨立的 SetSafeZoneImmune，不影響翻滾/特殊招式的動作幀無敵。
    /// </summary>
    public class SafeZone : MonoBehaviour
    {
        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            var player = other.GetComponent<PlayerController>();
            player?.SetSafeZoneImmune(true);
            Debug.Log("[SafeZone] 玩家進入善區，免疫惡區傷害。");
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            var player = other.GetComponent<PlayerController>();
            player?.SetSafeZoneImmune(false);
            Debug.Log("[SafeZone] 玩家離開善區。");
        }
    }
}