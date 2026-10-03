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
        public const string CREDITS_SCENE = "CreditsScene";

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

        /// <summary>
        /// 最近一次 Boss 戰是不是失敗收場（玩家死亡）。給過場圖場景判斷用：
        /// 失敗時播完過場圖一律回小木屋，不接結局名單，停留時間也比較短。
        /// 進入新的 Boss 場景、或 Boss 被擊敗時會清掉。
        /// </summary>
        public static bool LastBattleFailed { get; set; }

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

            // 直接在 Editor 開特定 Boss 場景測試（不經過小木屋 LoadBossScene()）時，
            // LastBossType 會停在預設的 None：死亡後「OO・心魔克服」提示的 Canvas 淡入淡出
            // 照常播放，但文字用 None 對不到任何 case、印出空字串，看起來就像「提示沒跳出來」；
            // 同一顆變數還會連帶讓進度紀錄記錯 Boss、結局過場圖片選錯池。這裡場景一載入就依
            // 場景名稱自我修正，不管是從小木屋進來還是直接在 Boss 場景按 Play，都會是對的。
            LastBossType = scene.name switch
            {
                GREED_SCENE   => BossType.Greed,
                WRATH_SCENE   => BossType.Wrath,
                FOOLISH_SCENE => BossType.Foolish,
                _             => LastBossType,
            };

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

        /// <summary>
        /// 依「目前作用中場景」解析出 Boss 類型並同步回 LastBossType，不需要 SceneTransitionManager
        /// 的實例存在——直接在 Editor 開特定 Boss 場景測試（不經過小木屋 LoadBossScene()）時就是
        /// 這種情況，LastBossType 會停在預設值 None。Boss 死亡當下（人還在 Boss 場景內）呼叫這個，
        /// 取代直接讀 LastBossType，避免死亡通知文字印出空字串、進度紀錄記錯 Boss、結局過場圖片
        /// 選錯池——這三個下游都共用同一顆 LastBossType，一起修正。
        /// </summary>
        public static BossType ResolveAndSyncCurrentBossType()
        {
            LastBossType = SceneManager.GetActiveScene().name switch
            {
                GREED_SCENE   => BossType.Greed,
                WRATH_SCENE   => BossType.Wrath,
                FOOLISH_SCENE => BossType.Foolish,
                _             => LastBossType,
            };
            return LastBossType;
        }

        private static string BossSceneName(BossType bossType) => bossType switch
        {
            BossType.Greed   => GREED_SCENE,
            BossType.Wrath   => WRATH_SCENE,
            BossType.Foolish => FOOLISH_SCENE,
            _                => HUB_SCENE
        };

        // 預載中的場景：已載到 90% 停住，等 LoadBossScene 同一關時直接啟用（見 PreloadBossScene）
        private AsyncOperation _preloadOp;
        private string _preloadScene;

        /// <summary>
        /// 先在背景把 Boss 場景載到 90% 停住（不啟用）。機制說明圖顯示期間呼叫，
        /// 玩家看圖的時間就是載入時間，按下跳過後 LoadBossScene 同一關就幾乎立刻進場。
        /// 只服務「預載之後一定會接著 LoadBossScene 同一關」的流程：停在 90% 的載入會擋住後面排隊的場景載入。
        /// </summary>
        public void PreloadBossScene(BossType bossType)
        {
            if (_isTransitioning || _preloadOp != null) return;
            _preloadScene = BossSceneName(bossType);
            _preloadOp = SceneManager.LoadSceneAsync(_preloadScene);
            _preloadOp.allowSceneActivation = false;
        }

        /// <summary>從小木屋進入 Boss 場景。</summary>
        public void LoadBossScene(BossType bossType)
        {
            if (_isTransitioning) return;
            LastBossType = bossType;
            LastBattleFailed = false;
            StartCoroutine(TransitionRoutine(BossSceneName(bossType)));
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

            AsyncOperation asyncLoad;
            if (_preloadOp != null && _preloadScene == targetScene)
            {
                asyncLoad = _preloadOp;   // 已預載好（或載到一半），接手繼續
            }
            else
            {
                asyncLoad = SceneManager.LoadSceneAsync(targetScene);
                asyncLoad.allowSceneActivation = false;
            }
            _preloadOp = null;
            _preloadScene = null;

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
            // 沒有淡入淡出用的 Canvas（例如小木屋場景的 TestBootstrap 管理器、或 Canvas 已隨場景被銷毀）時，
            // 不能直接 yield break：_isTransitioning 是在這個協程「最後」才設回 false 的，
            // 提早離開會讓它永遠卡在 true，之後所有 LoadXxx() 都被 `if (_isTransitioning) return;`
            // 默默擋掉——表現就是「失敗/通關提示播完了，卻怎麼都不切到過場圖、停在原場景」。
            if (_fadeCanvasGroup == null)
            {
                _isTransitioning = false;
                yield break;
            }

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
