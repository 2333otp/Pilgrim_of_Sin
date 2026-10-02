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
    ///
    /// 【勝負流程】
    ///   贏：等 _winDelay → 「OO • 心魔克服」通知 → 過場圖 → 小木屋（三隻都通關則接結局名單）
    ///   輸：等死亡動畫 _loseDelay → 戰鬥 UI 淡出 → 「OO • 心魔未克服」通知 → 過場圖（目前進度）→ 小木屋
    ///   勝負只會有一個生效：先發生的算，另一邊會被忽略（避免「打贏了還被重開」之類的衝突）。
    /// </summary>
    public class BossResultPortal : MonoBehaviour
    {
        public static BossResultPortal Instance { get; private set; }

        /// <summary>
        /// 玩家已經死亡、失敗流程已開始。Boss 控制器看到 true 就該停止追擊/攻擊，
        /// 不然玩家倒下後 Boss 還在追屍體、揮拳，畫面很怪。
        /// </summary>
        public static bool IsPlayerDefeated => Instance != null && Instance._playerDefeatedHandled;

        [Header("結果延遲（秒）")]
        [Tooltip("Boss死亡後幾秒才顯示通關通知（讓死亡動畫播完）")]
        [SerializeField] private float _winDelay = 2.5f;

        [Tooltip("玩家死亡後幾秒才開始淡出 UI 並顯示失敗提示（要等玩家死亡動畫播完，Muir 的死亡動畫約 3.97 秒）")]
        [SerializeField] private float _loseDelay = 4.0f;

        [Header("通關 / 失敗通知")]
        [Tooltip("Boss死亡動畫播完後顯示的「OO • 心魔克服」通知；玩家死亡時顯示「OO • 心魔未克服」，兩者共用同一份。")]
        [SerializeField] private LevelClearNotification _levelClearNotification;

        [Header("失敗時淡出的戰鬥 UI")]
        [Tooltip("玩家死亡後、失敗提示出現前要淡出的 UI 根物件（血條、天秤圖示、提示橫幅…）。\n" +
                 "不要放 LevelClearNotification、PauseCanvas。空的話會自動淡出場景裡的 CombatHUD。")]
        [SerializeField] private GameObject[] _hudRootsToHideOnDefeat;

        [SerializeField] private float _hudFadeDuration = 0.5f;

        private bool _bossDefeatedHandled;
        private bool _playerDefeatedHandled;

        private void Awake()
        {
            // 場景內單例（不跨場景）
            Instance = this;
        }

        /// <summary>
        /// Boss 被擊敗 → 流程圖：贏 → 回小木屋（或進入通關流程）。
        /// 只處理第一次呼叫，防止死亡瞬間多重觸發（例如同一幀多重攻擊判定）造成通知/切場景重複播放。
        /// 玩家已經先死了就忽略（先發生的算）。
        /// </summary>
        public void OnBossDefeated()
        {
            if (_bossDefeatedHandled || _playerDefeatedHandled) return;
            _bossDefeatedHandled = true;
            SceneTransitionManager.LastBattleFailed = false;

            // 直接在 Editor 開這個 Boss 場景測試（不經過小木屋 LoadBossScene()）時，
            // LastBossType 會停在預設值 None，害死亡通知的「OO • 心魔克服」印出空字串——
            // Canvas 照常淡入淡出，只是沒有文字，看起來就像提示沒跳出來。這裡趁人還在
            // Boss 場景內，依場景名稱把 LastBossType 修正回正確值，下面用到它的地方都受惠。
            SceneTransitionManager.ResolveAndSyncCurrentBossType();

            GameProgressManager.Instance?.MarkBossDefeated(SceneTransitionManager.LastBossType);
            GameProgressManager.Instance?.Save();
            PauseMenuUI.Instance?.NotifyBossDefeated();
            StartCoroutine(WinRoutine());
        }

        /// <summary>
        /// 玩家死亡 → 流程圖：輸 → 失敗提示 → 過場圖 → 回小木屋（玩家自己再選關卡）。
        /// 失敗不記錄進度。Boss 已經先被擊敗就忽略（先發生的算）。
        /// </summary>
        public void OnPlayerDefeated()
        {
            if (_playerDefeatedHandled || _bossDefeatedHandled) return;
            _playerDefeatedHandled = true;
            StartCoroutine(LoseRoutine());
        }

        private IEnumerator WinRoutine()
        {
            yield return new WaitForSeconds(_winDelay);

            if (_levelClearNotification != null)
            {
                bool notificationDone = false;
                _levelClearNotification.Show(SceneTransitionManager.LastBossType, () => notificationDone = true);
                while (!notificationDone) yield return null;
            }

            yield return StartCoroutine(GoToImageCutscene());
        }

        private IEnumerator LoseRoutine()
        {
            // 1. 等玩家死亡動畫播完（這段時間 Boss 已經因為 IsPlayerDefeated 停手）
            yield return new WaitForSeconds(_loseDelay);

            // 2. 戰鬥 UI 淡出，不然失敗提示會跟血條、提示橫幅疊在一起
            yield return StartCoroutine(FadeOutHud());

            // 3. 「OO • 心魔未克服」
            SceneTransitionManager.ResolveAndSyncCurrentBossType();
            if (_levelClearNotification != null)
            {
                bool notificationDone = false;
                _levelClearNotification.ShowFailure(SceneTransitionManager.LastBossType, () => notificationDone = true);
                while (!notificationDone) yield return null;
            }

            // 4. 過場圖（顯示目前進度），播完一律回小木屋，不接結局名單
            SceneTransitionManager.LastBattleFailed = true;
            yield return StartCoroutine(GoToImageCutscene());
        }

        /// <summary>
        /// 切到過場圖場景。正常走 SceneTransitionManager（有淡入淡出）；如果它因為任何原因沒有真的切走
        /// （旗標卡住、被擋掉…），等一下之後直接載入場景當保險，不讓玩家卡死在 Boss 場景。
        /// 切成功時場景會被卸載，這個協程隨之結束，不會重複載入。
        /// </summary>
        private IEnumerator GoToImageCutscene()
        {
            string current = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            if (SceneTransitionManager.Instance == null)
            {
                // 直接在 Editor 開 Boss 場景測試（沒有管理器）：直接載入
                UnityEngine.SceneManagement.SceneManager.LoadScene(SceneTransitionManager.IMAGE_CUTSCENE_SCENE);
                yield break;
            }

            SceneTransitionManager.Instance.LoadImageCutscene();
            yield return new WaitForSecondsRealtime(3f);

            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == current)
            {
                Debug.LogWarning("[BossResultPortal] 切換過場圖逾時沒有成功，改用直接載入。");
                UnityEngine.SceneManagement.SceneManager.LoadScene(SceneTransitionManager.IMAGE_CUTSCENE_SCENE);
            }
        }

        private IEnumerator FadeOutHud()
        {
            var roots = _hudRootsToHideOnDefeat;
            if (roots == null || roots.Length == 0)
            {
                var combatHud = FindFirstObjectByType<CombatHUD>();
                roots = combatHud != null ? new[] { combatHud.gameObject } : new GameObject[0];
            }

            var groups = new System.Collections.Generic.List<CanvasGroup>();
            foreach (var root in roots)
            {
                if (root == null) continue;
                var group = root.GetComponent<CanvasGroup>();
                if (group == null) group = root.AddComponent<CanvasGroup>();
                group.interactable = false;
                group.blocksRaycasts = false;
                groups.Add(group);
            }
            if (groups.Count == 0) yield break;

            float elapsed = 0f;
            float start = 1f;
            while (elapsed < _hudFadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float alpha = Mathf.Lerp(start, 0f, Mathf.Clamp01(elapsed / _hudFadeDuration));
                foreach (var g in groups) if (g != null) g.alpha = alpha;
                yield return null;
            }
            foreach (var g in groups) if (g != null) g.alpha = 0f;
        }
    }
}
