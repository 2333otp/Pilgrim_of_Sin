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
            // Time.timeScale == 0 代表 ESC 選單開著（PauseMenuUI 暫停時的作法），
            // 這時候互動鍵（R2）要留給選單的返回操作用，不能同時觸發傳送門。
            if (!_playerInRange || Time.timeScale == 0f) return;

            bool interactPressed = (Keyboard.current != null && Keyboard.current.xKey.wasPressedThisFrame)
                                || (Gamepad.current != null && Gamepad.current.rightTrigger.wasPressedThisFrame);
            if (interactPressed)
            {
                SceneTransitionManager.Instance?.LoadBossScene(_bossType);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                _playerInRange = true;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                _playerInRange = false;
            }
        }
    }
}