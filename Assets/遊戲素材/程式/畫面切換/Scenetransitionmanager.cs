using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PilgrimOfSin
{
    /// <summary>
    /// 場景切換管理器（單例）
    /// 負責所有場景間的切換，含淡入淡出效果。
    ///
    /// 【場景名稱對照】（請在 Unity Build Settings 中確認名稱一致）
    ///   主選單     → "MainScene"
    ///   小木屋     → "HubScene"
    ///   貪Boss房   → "GreedBossScene"
    ///   嗔Boss房   → "WrathBossScene"
    ///   癡Boss房   → "FoolishBossScene"
    ///
    /// 【使用方式】
    ///   主選單開始遊戲：SceneTransitionManager.Instance.LoadHubScene();
    ///   從小木屋進入關卡：SceneTransitionManager.Instance.LoadBossScene(BossType.Greed);
    ///   Boss死亡後回小木屋：SceneTransitionManager.Instance.ReturnToHub();
    ///   回主選單：SceneTransitionManager.Instance.ReturnToMainMenu();
    ///
    /// 【SceneTransitionManager 物件要放在哪？】
    ///   放在 MainScene。DontDestroyOnLoad 會讓它跟著跑完整個遊戲。
    /// </summary>
    public class SceneTransitionManager : MonoBehaviour
    {
        // ── 場景名稱常數（與 Build Settings 保持一致）────────────────
        public const string MAIN_SCENE = "MainScene";
        public const string HUB_SCENE = "HubScene";
        public const string GREED_SCENE = "GreedBossScene";
        public const string WRATH_SCENE = "WrathBossScene";
        public const string FOOLISH_SCENE = "FoolishBossScene";

        // ── 單例 ─────────────────────────────────────────────────────
        public static SceneTransitionManager Instance { get; private set; }

        // ── Inspector 設定 ────────────────────────────────────────────
        [Header("淡入淡出設定")]
        [SerializeField] private float _fadeDuration = 0.5f;

        [Header("Canvas Group（掛在全螢幕黑色 Image 上）")]
        [SerializeField] private CanvasGroup _fadeCanvasGroup;

        // ── 狀態 ─────────────────────────────────────────────────────
        private bool _isTransitioning = false;

        /// <summary>目前玩家是從哪個 Boss 房進來的（用於回小木屋時記錄）</summary>
        public static BossType LastBossType { get; private set; } = BossType.None;

        // ── 生命週期 ──────────────────────────────────────────────────
        private void Awake()
        {
            // 單例：跨場景保留
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // 一開始保持全黑
            if (_fadeCanvasGroup != null)
            {
                _fadeCanvasGroup.alpha = 1f;
                _fadeCanvasGroup.blocksRaycasts = false; // 不擋按鈕點擊
            }
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // MainScene 不執行淡入（玩家還沒按開始遊戲）
            if (scene.name == MAIN_SCENE) return;

            // 每次新場景載入後淡入
            StartCoroutine(FadeIn());
        }

        // ── 公開 API ──────────────────────────────────────────────────

        /// <summary>
        /// 從主選單進入小木屋。
        /// 流程圖：開始遊戲 → 劇情動畫 → 進小木屋地圖
        /// （劇情動畫請在 HubScene 的 Awake 中自行處理，
        ///  或在按下開始按鈕後先播完動畫再呼叫此方法）
        /// </summary>
        public void LoadHubScene()
        {
            if (_isTransitioning) return;

            Debug.Log("[SceneTransition] 進入小木屋");
            StartCoroutine(TransitionRoutine(HUB_SCENE));
        }

        /// <summary>
        /// 回到主選單（主選單的「返回選單」按鈕用）。
        /// 流程圖：小木屋 → 可返回選單 → 遊戲選單
        /// </summary>
        public void ReturnToMainMenu()
        {
            if (_isTransitioning) return;

            Debug.Log("[SceneTransition] 返回主選單");
            StartCoroutine(TransitionRoutine(MAIN_SCENE));
        }

        /// <summary>
        /// 從小木屋進入 Boss 關卡。
        /// 流程圖：選擇關卡 → 罪行前導動畫 → 挑戰BOSS
        /// （若有過場動畫，在 Boss 場景的 Awake 中自行處理）
        /// </summary>
        public void LoadBossScene(BossType bossType)
        {
            if (_isTransitioning) return;

            LastBossType = bossType;
            string sceneName = bossType switch
            {
                BossType.Greed => GREED_SCENE,
                BossType.Wrath => WRATH_SCENE,
                BossType.Foolish => FOOLISH_SCENE,
                _ => HUB_SCENE
            };

            Debug.Log($"[SceneTransition] 進入 {bossType} 場景：{sceneName}");
            StartCoroutine(TransitionRoutine(sceneName));
        }

        /// <summary>
        /// 從 Boss 房回到小木屋（贏或輸都走這條路）。
        /// 流程圖：贏/輸 → 小木屋
        /// </summary>
        public void ReturnToHub()
        {
            if (_isTransitioning) return;

            Debug.Log($"[SceneTransition] 回到小木屋");
            StartCoroutine(TransitionRoutine(HUB_SCENE));
        }

        /// <summary>
        /// 重新挑戰目前的 Boss（輸了重新開始）。
        /// 流程圖：輸 → 重新 → 挑戰BOSS
        /// </summary>
        public void RestartCurrentBoss()
        {
            if (_isTransitioning) return;

            string currentScene = SceneManager.GetActiveScene().name;
            Debug.Log($"[SceneTransition] 重新挑戰：{currentScene}");
            StartCoroutine(TransitionRoutine(currentScene));
        }

        // ── 協程：淡出 → 載入 → 淡入 ──────────────────────────────────
        private IEnumerator TransitionRoutine(string targetScene)
        {
            _isTransitioning = true;

            // 若畫面已經是全黑（如 MainScene 開始狀態），跳過淡出
            if (_fadeCanvasGroup == null || _fadeCanvasGroup.alpha < 1f)
                yield return StartCoroutine(FadeOut());

            // 載入場景
            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(targetScene);
            asyncLoad.allowSceneActivation = false;

            while (asyncLoad.progress < 0.9f)
                yield return null;

            asyncLoad.allowSceneActivation = true;
            yield return null;

            _isTransitioning = false;
        }

        private IEnumerator FadeOut()
        {
            if (_fadeCanvasGroup == null) yield break;

            float elapsed = 0f;
            _fadeCanvasGroup.blocksRaycasts = true;

            while (elapsed < _fadeDuration)
            {
                elapsed += Time.deltaTime;
                _fadeCanvasGroup.alpha = Mathf.Clamp01(elapsed / _fadeDuration);
                yield return null;
            }
            _fadeCanvasGroup.alpha = 1f;
        }

        private IEnumerator FadeIn()
        {
            if (_fadeCanvasGroup == null) yield break;

            float elapsed = 0f;
            _fadeCanvasGroup.alpha = 1f;

            while (elapsed < _fadeDuration)
            {
                elapsed += Time.deltaTime;
                _fadeCanvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / _fadeDuration);
                yield return null;
            }
            _fadeCanvasGroup.alpha = 0f;
            _fadeCanvasGroup.blocksRaycasts = false;
        }
    }

    // ── Boss 類型枚舉 ──────────────────────────────────────────────────
    public enum BossType
    {
        None,
        Greed,   // 貪
        Wrath,   // 嗔
        Foolish  // 癡
    }
}