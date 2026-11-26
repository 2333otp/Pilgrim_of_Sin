using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;

namespace PilgrimOfSin
{
    /// <summary>
    /// 開場操作引導（進關後暫停顯示）。掛在 PauseCanvas 底下的 OpeningGuide_Panel。
    ///
    /// · 每次進入 Boss 關卡時自動顯示（死掉重新挑戰同一關不顯示）；顯示期間遊戲暫停（timeScale=0），
    ///   玩家狀態機也暫停（PlayerController 檢查 IsOpen），所以 Options 不會打開 ESC 選單。
    /// · 8 頁，一頁只講一兩個操作。按該頁的按鍵 → 那一行亮起、右邊示範格換成那個動作。
    /// · 十字鍵／左搖桿 ←→ 翻頁、↑↓ 選擇該頁的項目（同武器介紹）；
    ///   「切換武器」頁只有一行，按 ↑↓ 就等於試按切換武器。
    /// · 長按 Options（鍵盤長按 Esc）約 1 秒關閉並開始戰鬥，避免誤觸。
    ///
    /// 根物件常駐啟用，只開關 Content 子物件（避開 Awake 自己 SetActive(false) 的陷阱）。
    /// 版面由 Editor 選單「PilgrimOfSin/UI/重建開場操作引導」產生，數值以 1920×1080 為準。
    /// 示範影片之後錄好，放進每一行的 demoClip 即可（沒放就顯示「（會放示意用影片）」）。
    /// </summary>
    public class OpeningGuidePanel : MonoBehaviour
    {
        public enum GuideKey { Move, Camera, ResetCamera, Jump, Roll, LockOn, LightAttack, HeavyAttack, Special, WeaponSwitch, Pause }

        [System.Serializable]
        public class IconLabel
        {
            public Sprite icon;
            [Tooltip("疊在 icon 上的圖（例：完整十字鍵上疊紅色的那一個方向）；可留空")]
            public Sprite overlay;
            public string label;
        }

        [System.Serializable]
        public class Row
        {
            public string name;
            [TextArea(1, 3)] public string desc;
            [Tooltip("第三行小字（可留空）")]
            public string extra;
            [Tooltip("第三行改用「圖示＋文字」排列（例：十字鍵四個方向對應的武器）。有填就不顯示 extra 文字。")]
            public IconLabel[] extraIcons;
            public GuideKey key;
            public Sprite glyph;
            [Tooltip("選中時的按鍵圖（紅色版）")]
            public Sprite glyphLit;
            [Tooltip("按鍵圖大小（1920 基準）")]
            public Vector2 glyphSize = new Vector2(63f, 63f);
            [Tooltip("示範影片（之後錄好再放；留空 = 顯示「（會放示意用影片）」）")]
            public VideoClip demoClip;
        }

        [System.Serializable]
        public class ComboLine
        {
            public Sprite[] keys;
            public string text;
        }

        [System.Serializable]
        public class Page
        {
            public string title;
            public Row[] rows;
            [Tooltip("連段範例（只有攻擊連段頁有）")]
            public ComboLine[] combos;
            [Tooltip("天地線框內最下方的提示（可留空）")]
            public string note;
        }

        [System.Serializable]
        public class RowUI
        {
            public RectTransform root;
            public Image band;
            public Image bar;
            public Image glyph;
            public TMP_Text nameText;
            public TMP_Text descText;
            public TMP_Text extraText;
            public RectTransform iconLine;
            public Image[] iconLineIcons;
            public Image[] iconLineOverlays;
            public TMP_Text[] iconLineLabels;
        }

        [System.Serializable]
        public class ComboLineUI
        {
            public RectTransform root;
            public Image[] keys;
            public TMP_Text text;
        }

        public static OpeningGuidePanel Instance { get; private set; }

        /// <summary>引導開著時為 true：PlayerController 據此暫停狀態機（不會攻擊、也不會打開 ESC 選單）。</summary>
        public static bool IsOpen { get; private set; }

        // 何時自動出現（2026-11-26 使用者定案）：每次「進入」Boss 關都出現；死掉後重新挑戰同一關（場景名稱跟上一個一樣）不出現。
        private static string _lastSceneName;
        private static bool _autoShowPending;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { IsOpen = false; Instance = null; _lastSceneName = null; _autoShowPending = false; }

        // 有些場景把 PauseCanvas 存成關閉（第一次按暫停時 PausedState 才把它打開），
        // 這樣引導的 Start 永遠不會跑。所以場景一載入就檢查：要顯示引導 → 先把 PauseCanvas 打開。
        // （PauseMenuUI.Awake 會把所有選單面板藏好，提早打開跟第一次按暫停時打開是一樣的。）
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void HookSceneLoaded()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        // 專案的 Enter Play Mode 設定關掉了場景重新載入：直接在 Boss 場景按 Play 時不會送 sceneLoaded，這裡補檢查一次
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CheckFirstScene()
            => OnSceneLoaded(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), UnityEngine.SceneManagement.LoadSceneMode.Single);

        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            // 同一個場景再載入一次 = 重新挑戰（或同一次載入被檢查兩次），不重複顯示
            if (scene.name == _lastSceneName) return;
            _lastSceneName = scene.name;

            foreach (var go in scene.GetRootGameObjects())
            {
                var menu = go.GetComponentInChildren<PauseMenuUI>(true);
                if (menu == null || !menu.IsBossScene) continue;
                if (menu.GetComponentInChildren<OpeningGuidePanel>(true) == null) continue;
                _autoShowPending = true;
                if (!menu.gameObject.activeSelf) menu.gameObject.SetActive(true);
                return;
            }
        }

        [Header("根")]
        [SerializeField] private GameObject _content;
        [SerializeField] private CanvasGroup _contentGroup;

        [Header("標題與頁碼")]
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _pageIndicator;
        [SerializeField] private Button _leftArrow;
        [SerializeField] private Button _rightArrow;

        [Header("左：操作清單（座標以模擬頁 1280×720 為單位，執行時 ×1.5）")]
        [SerializeField] private RowUI[] _rowUIs = new RowUI[2];
        [SerializeField] private RectTransform _combosRoot;
        [SerializeField] private ComboLineUI[] _comboUIs = new ComboLineUI[2];
        [SerializeField] private TMP_Text _noteText;

        [Header("右：示範格")]
        [SerializeField] private TMP_Text _demoCaption;
        [SerializeField] private GameObject _demoPlaceholder;
        [SerializeField] private RawImage _demoVideo;
        [SerializeField] private VideoPlayer _videoPlayer;

        [Header("底部：長按 Options 開始戰鬥")]
        [SerializeField] private Image _holdRing;
        [SerializeField] private float _holdSeconds = 1f;

        [Header("顯示")]
        [Tooltip("打開後這段時間內不接受按鍵（避免進關前按著的鍵直接觸發）")]
        [SerializeField] private float _inputDelay = 0.4f;
        [SerializeField] private float _fadeInSeconds = 0.3f;

        [Header("清單顏色")]
        [SerializeField] private Color _nameOn = new Color32(0xF3, 0xDC, 0xA6, 0xFF);
        [SerializeField] private Color _nameOff = new Color32(0xD9, 0xB3, 0x6A, 0xFF);

        [Header("頁面資料")]
        [SerializeField] private Page[] _pages;

        private int _page;
        private int _sel;
        private float _openedAt;
        private float _hold;
        private bool _holdArmed;
        private bool _leftStickWasOn, _rightStickWasOn;
        private RenderTexture _videoRT;
        private System.Action _onClosed;

        private const float U = 1.5f; // 模擬頁 → 1920 的倍率

        private void Awake()
        {
            Instance = this;
            if (_content != null) _content.SetActive(false);
            if (_leftArrow != null) _leftArrow.onClick.AddListener(() => FlipPage(-1));
            if (_rightArrow != null) _rightArrow.onClick.AddListener(() => FlipPage(1));
        }

        private void Start()
        {
            // 進入 Boss 關卡：自動顯示（重新挑戰不顯示，見 OnSceneLoaded）
            var menu = GetComponentInParent<PauseMenuUI>(true);
            if (_autoShowPending && menu != null && menu.IsBossScene)
            {
                _autoShowPending = false;
                Open();
            }
        }

        private void OnDestroy()
        {
            if (IsOpen) { IsOpen = false; Time.timeScale = 1f; }
            if (Instance == this) Instance = null;
            if (_videoRT != null) { _videoRT.Release(); Destroy(_videoRT); }
        }

        /// <summary>打開引導。onClosed：關閉後要做的事（從 ESC 選單重看時用來回到選單）；null = 關閉後直接回到遊戲。</summary>
        public void Open(System.Action onClosed = null)
        {
            _onClosed = onClosed;
            _page = 0;
            _sel = 0;
            _hold = 0f;
            _holdArmed = false;
            _openedAt = Time.unscaledTime;
            IsOpen = true;
            Time.timeScale = 0f;
            if (_content != null) _content.SetActive(true);
            if (_contentGroup != null) _contentGroup.alpha = _fadeInSeconds > 0f ? 0f : 1f;
            Refresh();
        }

        private void Close()
        {
            StopVideo();
            if (_content != null) _content.SetActive(false);
            IsOpen = false;

            var cb = _onClosed;
            _onClosed = null;
            if (cb != null) { cb(); return; }

            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Update()
        {
            if (!IsOpen) return;

            // 別的系統（例如場景剛載入時的流程）可能把時間恢復，引導開著就一律維持暫停
            if (_onClosed == null) Time.timeScale = 0f;

            if (_contentGroup != null && _contentGroup.alpha < 1f)
                _contentGroup.alpha = Mathf.Clamp01((Time.unscaledTime - _openedAt) / Mathf.Max(0.01f, _fadeInSeconds));

            if (Time.unscaledTime - _openedAt < _inputDelay)
            {
                // 延遲期間也要記住搖桿狀態，避免一結束就把「一直推著」誤判成剛推下去
                ReadSticks(out _, out _);
                return;
            }

            if (HandleHold()) return;
            HandleFlip();
            HandleSelect();
            HandleTryKeys();
        }

        // ── 長按 Options（鍵盤 Esc）關閉 ───────────────────────────
        // 同時接兩支手把時 Gamepad.current 會在兩支之間跳，所以一律看「所有手把」
        private static bool AnyPressed(System.Func<Gamepad, UnityEngine.InputSystem.Controls.ButtonControl> pick)
        {
            foreach (var g in Gamepad.all) if (pick(g).wasPressedThisFrame) return true;
            return false;
        }

        private static bool AnyHeld(System.Func<Gamepad, UnityEngine.InputSystem.Controls.ButtonControl> pick)
        {
            foreach (var g in Gamepad.all) if (pick(g).isPressed) return true;
            return false;
        }

        private static float MaxStick(bool left)
        {
            float m = 0f;
            foreach (var g in Gamepad.all) m = Mathf.Max(m, (left ? g.leftStick : g.rightStick).ReadValue().magnitude);
            return m;
        }

        private bool HandleHold()
        {
            var kb = Keyboard.current;
            bool pressedNow = AnyPressed(g => g.startButton) || (kb != null && kb.escapeKey.wasPressedThisFrame);
            bool held = AnyHeld(g => g.startButton) || (kb != null && kb.escapeKey.isPressed);

            // 只算「引導打開後才按下去」的那一次，進關前按著不放的不算
            if (pressedNow)
            {
                _holdArmed = true;
                Press(GuideKey.Pause);
            }
            if (!held) _holdArmed = false;

            _hold = _holdArmed && held ? _hold + Time.unscaledDeltaTime : 0f;
            if (_holdRing != null) _holdRing.fillAmount = Mathf.Clamp01(_hold / Mathf.Max(0.05f, _holdSeconds));

            if (_hold >= _holdSeconds)
            {
                _hold = 0f;
                if (_holdRing != null) _holdRing.fillAmount = 0f;
                Close();
                return true;
            }
            return false;
        }

        // ── 翻頁：只用十字鍵 ←→（鍵盤方向鍵）─────────────────────
        private void HandleFlip()
        {
            var kb = Keyboard.current;
            if (AnyPressed(g => g.dpad.left) || AnyPressed(g => g.leftStick.left) || (kb != null && kb.leftArrowKey.wasPressedThisFrame)) FlipPage(-1);
            else if (AnyPressed(g => g.dpad.right) || AnyPressed(g => g.leftStick.right) || (kb != null && kb.rightArrowKey.wasPressedThisFrame)) FlipPage(1);
        }

        // ── 上下選擇：十字鍵／左搖桿 ↑↓（鍵盤方向鍵），頭尾相接；同武器介紹 ───────────────
        private void HandleSelect()
        {
            var kb = Keyboard.current;
            int dir = 0;
            if (AnyPressed(g => g.dpad.up) || AnyPressed(g => g.leftStick.up) || (kb != null && kb.upArrowKey.wasPressedThisFrame)) dir = -1;
            else if (AnyPressed(g => g.dpad.down) || AnyPressed(g => g.leftStick.down) || (kb != null && kb.downArrowKey.wasPressedThisFrame)) dir = 1;
            if (dir == 0) return;
            int n = CurrentRows().Length;
            if (n <= 1) return;
            _sel = (_sel + dir + n) % n;
            Refresh();
        }

        private void FlipPage(int dir)
        {
            if (_pages == null || _pages.Length == 0) return;
            int next = Mathf.Clamp(_page + dir, 0, _pages.Length - 1);
            if (next == _page) return;
            _page = next;
            _sel = 0;
            Refresh();
        }

        // ── 試按：只有這一頁有的按鍵才有反應 ────────────────────────
        private void HandleTryKeys()
        {
            ReadSticks(out bool leftPushed, out bool rightPushed);

            if (leftPushed) Press(GuideKey.Move);
            if (rightPushed) Press(GuideKey.Camera);
            if (AnyPressed(g => g.leftTrigger)) Press(GuideKey.ResetCamera);
            if (AnyPressed(g => g.leftShoulder)) Press(GuideKey.LockOn);
            if (AnyPressed(g => g.buttonSouth)) Press(GuideKey.Jump);
            if (AnyPressed(g => g.buttonEast)) Press(GuideKey.Roll);
            if (AnyPressed(g => g.buttonWest)) Press(GuideKey.LightAttack);
            if (AnyPressed(g => g.buttonNorth)) Press(GuideKey.HeavyAttack);
            if (AnyPressed(g => g.rightShoulder)) Press(GuideKey.Special);
            if (AnyPressed(g => g.dpad.up) || AnyPressed(g => g.dpad.down)) Press(GuideKey.WeaponSwitch);
        }

        /// <summary>搖桿「剛推下去」的那一幀（推過一半才算）。</summary>
        private void ReadSticks(out bool leftPushed, out bool rightPushed)
        {
            bool l = MaxStick(true) > 0.5f;
            bool r = MaxStick(false) > 0.5f;
            leftPushed = l && !_leftStickWasOn;
            rightPushed = r && !_rightStickWasOn;
            _leftStickWasOn = l;
            _rightStickWasOn = r;
        }

        private void Press(GuideKey key)
        {
            var rows = CurrentRows();
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].key != key) continue;
                if (_sel != i) { _sel = i; Refresh(); }
                return;
            }
            // 這一頁沒有這個按鍵：不反應
        }

        private Row[] CurrentRows()
        {
            if (_pages == null || _pages.Length == 0) return new Row[0];
            return _pages[_page].rows ?? new Row[0];
        }

        // ── 畫面更新 ───────────────────────────────────────────────
        private void Refresh()
        {
            if (_pages == null || _pages.Length == 0) return;
            var page = _pages[_page];
            var rows = page.rows ?? new Row[0];
            _sel = Mathf.Clamp(_sel, 0, Mathf.Max(0, rows.Length - 1));

            if (_titleText != null)
                _titleText.text = $"<b>操作引導</b>   <size=60%><color=#CFC4AD><cspace=0.3em>{page.title}</cspace></color></size>";

            if (_pageIndicator != null)
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < _pages.Length; i++)
                    sb.Append(i == _page ? "<color=#D9B36A>◆</color>" : "<color=#5A4E3C>◆</color>").Append(i < _pages.Length - 1 ? " " : "");
                sb.Append($"     <color=#B9AD94>{_page + 1} / {_pages.Length}</color>");
                _pageIndicator.text = sb.ToString();
            }

            if (_leftArrow != null) _leftArrow.gameObject.SetActive(_page > 0);
            if (_rightArrow != null) _rightArrow.gameObject.SetActive(_page < _pages.Length - 1);

            // 每列等高：列數少的頁面列距拉大，整組在天地線範圍內（190~550）垂直置中；有連段範例的頁面靠上
            int n = rows.Length;
            bool hasCombos = page.combos != null && page.combos.Length > 0;
            bool hasNote = !string.IsNullOrEmpty(page.note);
            float rowH = hasCombos ? 92f : n >= 4 ? 80f : n == 3 ? 92f : n == 2 ? 108f : 124f;
            float listTop = hasCombos ? 204f : 190f + (hasNote ? 6f : 0f) + Mathf.Max(0f, (360f - n * rowH - (hasNote ? 40f : 0f)) / 2f);

            for (int i = 0; i < _rowUIs.Length; i++)
            {
                var ui = _rowUIs[i];
                if (ui == null || ui.root == null) continue;
                bool show = i < n;
                ui.root.gameObject.SetActive(show);
                if (!show) continue;
                FillRow(ui, rows[i], i == _sel, listTop + i * rowH, rowH - 8f);
            }

            if (_combosRoot != null)
            {
                _combosRoot.gameObject.SetActive(hasCombos);
                if (hasCombos)
                {
                    SetMockPos(_combosRoot, 136f, listTop + n * rowH + 2f);
                    for (int i = 0; i < _comboUIs.Length; i++)
                    {
                        var c = _comboUIs[i];
                        if (c == null || c.root == null) continue;
                        bool show = i < page.combos.Length;
                        c.root.gameObject.SetActive(show);
                        if (!show) continue;
                        var line = page.combos[i];
                        for (int k = 0; k < c.keys.Length; k++)
                        {
                            bool has = line.keys != null && k < line.keys.Length && line.keys[k] != null;
                            c.keys[k].gameObject.SetActive(has);
                            if (has) c.keys[k].sprite = line.keys[k];
                        }
                        if (c.text != null) c.text.text = line.text;
                    }
                }
            }

            if (_noteText != null)
            {
                _noteText.gameObject.SetActive(hasNote);
                _noteText.text = page.note;
            }

            var cur = n > 0 ? rows[_sel] : null;
            if (_demoCaption != null) _demoCaption.text = cur != null ? "示範：" + cur.name : string.Empty;
            PlayDemo(cur != null ? cur.demoClip : null);
        }

        private void FillRow(RowUI ui, Row row, bool on, float mockTop, float mockHeight)
        {
            ui.root.anchoredPosition = new Vector2(ui.root.anchoredPosition.x, 540f - mockTop * U);
            ui.root.sizeDelta = new Vector2(ui.root.sizeDelta.x, mockHeight * U);

            if (ui.band != null) ui.band.enabled = on;
            if (ui.bar != null) ui.bar.enabled = on;
            if (ui.glyph != null)
            {
                ui.glyph.sprite = on && row.glyphLit != null ? row.glyphLit : row.glyph;
                ui.glyph.rectTransform.sizeDelta = row.glyphSize;
            }
            if (ui.nameText != null) { ui.nameText.text = row.name; ui.nameText.color = on ? _nameOn : _nameOff; }
            if (ui.descText != null) ui.descText.text = row.desc;

            bool icons = row.extraIcons != null && row.extraIcons.Length > 0;
            if (ui.extraText != null)
            {
                bool hasExtra = !icons && !string.IsNullOrEmpty(row.extra);
                ui.extraText.gameObject.SetActive(hasExtra);
                ui.extraText.text = row.extra;
            }
            if (ui.iconLine != null)
            {
                ui.iconLine.gameObject.SetActive(icons);
                for (int k = 0; k < ui.iconLineIcons.Length; k++)
                {
                    bool has = icons && k < row.extraIcons.Length;
                    ui.iconLineIcons[k].gameObject.SetActive(has);
                    ui.iconLineLabels[k].gameObject.SetActive(has);
                    if (!has) continue;
                    ui.iconLineIcons[k].sprite = row.extraIcons[k].icon;
                    ui.iconLineLabels[k].text = row.extraIcons[k].label;
                    if (ui.iconLineOverlays != null && k < ui.iconLineOverlays.Length && ui.iconLineOverlays[k] != null)
                    {
                        var ov = row.extraIcons[k].overlay;
                        ui.iconLineOverlays[k].sprite = ov;
                        ui.iconLineOverlays[k].enabled = ov != null;
                    }
                }
            }
        }

        private static void SetMockPos(RectTransform rt, float mx, float my)
            => rt.anchoredPosition = new Vector2(mx * U - 960f, 540f - my * U);

        // ── 示範影片 ───────────────────────────────────────────────
        private void PlayDemo(VideoClip clip)
        {
            bool has = clip != null && _videoPlayer != null && _demoVideo != null;
            if (_demoPlaceholder != null) _demoPlaceholder.SetActive(!has);
            if (!has) { StopVideo(); return; }

            if (_videoRT == null)
            {
                _videoRT = new RenderTexture(896, 720, 0) { name = "OpeningGuideDemoRT" };
                _videoRT.Create();
            }
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.targetTexture = _videoRT;
            _videoPlayer.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime; // 引導開著時 timeScale=0，影片照樣要播
            _videoPlayer.isLooping = true;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            _demoVideo.texture = _videoRT;
            _demoVideo.enabled = true;
            if (_videoPlayer.clip != clip || !_videoPlayer.isPlaying)
            {
                _videoPlayer.clip = clip;
                _videoPlayer.Play();
            }
        }

        private void StopVideo()
        {
            if (_videoPlayer != null && _videoPlayer.isPlaying) _videoPlayer.Stop();
            if (_demoVideo != null) _demoVideo.enabled = false;
        }
    }
}
