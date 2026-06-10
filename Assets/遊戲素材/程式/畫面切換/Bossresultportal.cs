using System.Collections;
using UnityEngine;

namespace PilgrimOfSin
{
    /// <summary>
    /// Boss 戰結果處理器。
    /// 掛在 Boss 物件上（或場景管理物件上）。
    ///
    /// 【使用方式】
    ///   Boss 死亡時：BossResultPortal.Instance.OnBossDefeated();
    ///   玩家死亡時：BossResultPortal.Instance.OnPlayerDefeated();
    ///
    ///   也可從 BossController 的 OnDeath() 直接呼叫。
    /// </summary>
    public class BossResultPortal : MonoBehaviour
    {
        public static BossResultPortal Instance { get; private set; }

        [Header("結果延遲（秒）")]
        [Tooltip("Boss死亡後幾秒才切換場景（讓死亡動畫播完）")]
        [SerializeField] private float _winDelay = 2.5f;

        [Tooltip("玩家死亡後幾秒才切換（讓死亡動畫播完）")]
        [SerializeField] private float _loseDelay = 2.0f;

        [Header("失敗選項")]
        [Tooltip("true = 失敗後重新挑戰同一Boss；false = 回小木屋")]
        [SerializeField] private bool _restartOnLose = true;

        private void Awake()
        {
            // 場景內單例（不跨場景）
            Instance = this;
        }

        /// <summary>
        /// Boss 被擊敗 → 流程圖：贏 → 回小木屋（或進入通關流程）
        /// </summary>
        public void OnBossDefeated()
        {
            Debug.Log("[BossResult] Boss 被擊敗！準備返回小木屋...");
            StartCoroutine(WinRoutine());
        }

        /// <summary>
        /// 玩家死亡 → 流程圖：輸 → 重新 or 回小木屋
        /// </summary>
        public void OnPlayerDefeated()
        {
            Debug.Log("[BossResult] 玩家死亡！");
            StartCoroutine(LoseRoutine());
        }

        private IEnumerator WinRoutine()
        {
            yield return new WaitForSeconds(_winDelay);

            if (SceneTransitionManager.Instance != null)
                SceneTransitionManager.Instance.LoadImageCutscene();
            else
                UnityEngine.SceneManagement.SceneManager.LoadScene(
                    SceneTransitionManager.IMAGE_CUTSCENE_SCENE);
        }

        private IEnumerator LoseRoutine()
        {
            yield return new WaitForSeconds(_loseDelay);

            if (SceneTransitionManager.Instance != null)
            {
                if (_restartOnLose)
                    SceneTransitionManager.Instance.RestartCurrentBoss();
                else
                    SceneTransitionManager.Instance.ReturnToHub();
            }
            else
            {
                // 直接從 Boss 場景 Play 測試時的 fallback（沒有 SceneTransitionManager）
                UnityEngine.SceneManagement.SceneManager.LoadScene(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
            }
        }
    }
}