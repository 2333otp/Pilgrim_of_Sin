using UnityEngine;
using UnityEngine.InputSystem;

namespace PilgrimOfSin
{
    /// <summary>
    /// 掛在 HubScene 的傳送門物件上。
    /// 玩家走進觸發區後按互動鍵（X）進入對應 Boss 場景。
    /// </summary>
    public class ScenePortal : MonoBehaviour
    {
        [Header("設定")]
        [SerializeField] private SceneTransitionManager.BossType _bossType;

        private bool _playerInRange = false;

        private void Update()
        {
            Debug.Log($"[ScenePortal] Update: PlayerInRange={_playerInRange}");
            if (!_playerInRange) return;

            // 新版 Input System：直接讀鍵盤 X 鍵（互動鍵）
            if (Keyboard.current.xKey.wasPressedThisFrame)
            {
                SceneTransitionManager.Instance?.LoadBossScene(_bossType);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                _playerInRange = true;
                Debug.Log($"[ScenePortal] 玩家進入 {_bossType} 傳送門範圍");
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                _playerInRange = false;
                Debug.Log($"[ScenePortal] 玩家離開 {_bossType} 傳送門範圍");
            }
        }
    }
}