using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

namespace PilgrimOfSin
{
    public class SceneTransitionManager : MonoBehaviour
    {
        // ── 場景名稱常數（與 Build Settings 保持一致）──────────────────
        public const string MAIN_SCENE = "MainScene";
        public const string CUTSCENE_SCENE = "CutsceneScene";
        public const string IMAGE_CUTSCENE_SCENE = "ImageCutsceneScene";
        public const string HUB_SCENE = "HubScene";
        public const string GREED_SCENE = "GreedBossScene";
        public const string WRATH_SCENE = "WrathBossScene";
        public const string FOOLISH_SCENE = "FoolishBossScene";

        // ── 單例 ────────────────────────────────────────────────────────
        public static SceneTransitionManager Instance { get; private set; }

        // ── Inspector 設定 ──────────────────────────────────────────────
        [Header("淡入淡出設定")]
        [SerializeField] private float _fadeDuration = 0.5f;

        [Header("Canvas Group（掛在全螢幕黑色 Image 上）")]
        [SerializeField] private CanvasGroup _fadeCanvasGroup;

        // ── 狀態 ────────────────────────────────────────────────────────
        private bool _isTransitioning = false;

        /// <summary>目前最後啟動的 Boss 場景（用於小木屋返回後記憶）</summary>
        public static BossType LastBossType { get; private set; } = BossType.None;

        // ── 生命週期 ─────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (_fadeCanvasGroup != null)
            {
                _fadeCanvasGroup.alpha = 1f;
                _fadeCanvasGroup.blocksRaycasts = false;
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
            bool needsCursor = scene.name == MAIN_SCENE;
            Cursor.lockState = needsCursor ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible   = needsCursor;

            if (scene.name == MAIN_SCENE)
            {
                _isTransitioning = false;
                return;
            }

            if (scene.name == CUTSCENE_SCENE)
            {
                _isTransitioning = false;
                return;
            }

            if (scene.name == IMAGE_CUTSCENE_SCENE)
            {
                _isTransitioning = false;
                if (_fadeCanvasGroup != null) _fadeCanvasGroup.alpha = 0f;
                return;
            }

            StartCoroutine(FadeIn());
        }

        // ── 公開 API ─────────────────────────────────────────────────────

        /// <summary>從主選單進入過場動畫。</summary>
        public void LoadCutscene()
        {
            if (_isTransitioning) return;
            StartCoroutine(TransitionRoutine(CUTSCENE_SCENE));
        }

        /// <summary>Boss 擊敗後進入過場圖片場景，結束後自動回小木屋。</summary>
        public void LoadImageCutscene()
        {
            if (_isTransitioning) return;
            StartCoroutine(TransitionRoutine(IMAGE_CUTSCENE_SCENE));
        }

        /// <summary>從主選單直接進入小木屋（跳過過場，測試用）。</summary>
        public void LoadHubScene()
        {
            if (_isTransitioning) return;
            StartCoroutine(TransitionRoutine(HUB_SCENE));
        }

        /// <summary>回到主選單。</summary>
        public void ReturnToMainMenu()
        {
            if (_isTransitioning) return;
            StartCoroutine(TransitionRoutine(MAIN_SCENE));
        }

        /// <summary>從小木屋進入 Boss 場景。</summary>
        public void LoadBossScene(BossType bossType)
        {
            if (_isTransitioning) return;
            LastBossType = bossType;
            string sceneName = bossType switch
            {
                BossType.Greed   => GREED_SCENE,
                BossType.Wrath   => WRATH_SCENE,
                BossType.Foolish => FOOLISH_SCENE,
                _                => HUB_SCENE
            };
            StartCoroutine(TransitionRoutine(sceneName));
        }

        /// <summary>從 Boss 場景回到小木屋（贏或輸）。</summary>
        public void ReturnToHub()
        {
            if (_isTransitioning) return;
            StartCoroutine(TransitionRoutine(HUB_SCENE));
        }

        /// <summary>重新挑戰目前的 Boss（輸了重試）。</summary>
        public void RestartCurrentBoss()
        {
            if (_isTransitioning) return;
            string currentScene = SceneManager.GetActiveScene().name;
            StartCoroutine(TransitionRoutine(currentScene));
        }

        // ── 核心：淡出 → 載入 → 淡入 ────────────────────────────────────
        private IEnumerator TransitionRoutine(string targetScene)
        {
            _isTransitioning = true;

            if (_fadeCanvasGroup == null || _fadeCanvasGroup.alpha < 1f)
                yield return StartCoroutine(FadeOut());

            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(targetScene);
            asyncLoad.allowSceneActivation = false;

            while (asyncLoad.progress < 0.9f)
                yield return null;

            asyncLoad.allowSceneActivation = true;
            yield return null;
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
            _isTransitioning = false;
        }

        // ── Boss 類型枚舉 ─────────────────────────────────────────────────
        public enum BossType
        {
            None,
            Greed,   // 貪
            Wrath,   // 嗔
            Foolish, // 癡
        }
    }
}
