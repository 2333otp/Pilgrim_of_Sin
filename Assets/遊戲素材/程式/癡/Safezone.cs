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
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            var player = other.GetComponent<PlayerController>();
            player?.SetSafeZoneImmune(false);
        }
    }
}