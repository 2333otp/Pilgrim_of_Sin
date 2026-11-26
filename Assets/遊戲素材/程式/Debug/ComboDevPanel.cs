#if UNITY_EDITOR || DEVELOPMENT_BUILD

using UnityEngine;
using UnityEngine.SceneManagement;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 開發者面板（F1 開關，只在 Editor / Development Build 存在）：
    ///   · 手把圖：直接沿用 ESC 選單的 GamepadDiagram.prefab，按下的鍵會亮紅（GlyphHighlightIcon）。
    ///   · 連段進度圖：仿無雙的出招表格式，一般攻擊 N1→N4、強攻擊 C1~C4（斜線連到對應的 N）、秘劍 R1。
    ///     正在出的招亮紅並顯示播放進度與取消時間點刻度，已打過的路徑亮起，已預輸入的下一招以黃框標示。
    /// 由 FSMDebugOverlay 建立並同步顯示狀態；圖檔在 Editor 用 AssetDatabase 讀取（Development Build 沒有圖檔時改用文字）。
    /// </summary>
    public class ComboDevPanel : MonoBehaviour
    {
        public bool Visible { get; set; } = true;
        public PlayerController Player { get; set; }

        // ── 圖檔路徑（UI圖檔/操作） ───────────────────────────────────
        private const string IconRoot = "Assets/遊戲素材/UI圖檔/操作/";
        private const string GamepadPrefabPath = "Assets/遊戲素材/程式/畫面切換/GamepadDiagram.prefab";

        private Texture2D _squareIdle, _squareLit, _triangleIdle, _triangleLit, _r1Idle, _r1Lit;
        private bool _texturesLoaded;

        // ── 手把圖（uGUI） ─────────────────────────────────────────────
        private GameObject _gamepadCanvas;
        private RectTransform _gamepadHolder;

        // ── 連段狀態（攻擊結束後保留一下，方便看清楚剛剛打了什麼） ──────
        private const float HoldSeconds = 1.0f;
        private struct Snapshot
        {
            public bool active;         // 正在 MoveAttack
            public MoveAttackState.MoveKind kind;
            public bool isCharge;
            public int index;
            public float progress;
            public float inputStart;
            public float cancelRatio;
            public string clipName;
            public float duration;
            public bool bufLight, bufHeavy, bufRoll, bufPause;
            public bool awaitingHold;   // △ 正在判斷短按／長按
            public int pendingStrong;   // 判斷完短按會出的 C
            public bool pauseEligible;  // 這一招之後可以接停頓連打
            public System.Collections.Generic.List<HitWindow> windows;   // 命中區間（null / 空 = 整招一下）
            public int activeWindow;    // 目前開著的命中區間
            public bool special;        // 正在放特殊技（秘劍）
        }
        private Snapshot _snap;
        private float _lastActiveTime = -99f;

        // ── IMGUI ──────────────────────────────────────────────────────
        private float _s = 1f;
        private GUIStyle _box, _title, _label, _small, _center;
        private float _styleScale = -1f;
        private Texture2D _white;

        private static readonly Color Dim = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color Lit = new Color(1f, 0.85f, 0.4f, 1f);
        private static readonly Color Current = new Color(1f, 0.25f, 0.3f, 1f);
        private static readonly Color Buffered = new Color(1f, 0.95f, 0.2f, 1f);

        // ════════════════════════════════════════════════════════════
        private void Awake()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            _white = Texture2D.whiteTexture;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (_gamepadCanvas) Destroy(_gamepadCanvas);
        }

        // 換場景（例如重新挑戰）後 PlayerInput 是新的，手把圖重建才會綁到新的輸入
        private void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            if (_gamepadCanvas) Destroy(_gamepadCanvas);
            _gamepadCanvas = null;
        }

        private void Update()
        {
            UpdateGamepad();
            UpdateSnapshot();
        }

        // ── 手把圖 ─────────────────────────────────────────────────────
        private void UpdateGamepad()
        {
            if (!Visible || PilgrimOfSin.OpeningGuidePanel.IsOpen) // 開場引導開著時也先收起來
            {
                if (_gamepadCanvas && _gamepadCanvas.activeSelf) _gamepadCanvas.SetActive(false);
                return;
            }
            if (_gamepadCanvas == null) CreateGamepad();
            if (_gamepadCanvas == null) return;
            if (!_gamepadCanvas.activeSelf) _gamepadCanvas.SetActive(true);

            // 手把圖高度 = 畫面高度 28%，貼右下角
            float scale = Screen.height * 0.28f / 900f;
            _gamepadHolder.localScale = new Vector3(scale, scale, 1f);
            _gamepadHolder.anchoredPosition = new Vector2(-10f, 10f);
        }

        private void CreateGamepad()
        {
            GameObject prefab = null;
#if UNITY_EDITOR
            prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(GamepadPrefabPath);
#endif
            if (prefab == null) return;

            _gamepadCanvas = new GameObject("[Dev Gamepad Canvas]");
            _gamepadCanvas.transform.SetParent(transform, false);
            var canvas = _gamepadCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 4000;

            var holderGo = new GameObject("Holder", typeof(RectTransform));
            holderGo.transform.SetParent(_gamepadCanvas.transform, false);
            _gamepadHolder = (RectTransform)holderGo.transform;
            _gamepadHolder.anchorMin = _gamepadHolder.anchorMax = new Vector2(1f, 0f);
            _gamepadHolder.pivot = new Vector2(1f, 0f);
            _gamepadHolder.sizeDelta = new Vector2(1600f, 900f);

            var diagram = Instantiate(prefab, _gamepadHolder);
            var rt = (RectTransform)diagram.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.localScale = Vector3.one;
            // 開發者面板只是用來看，不該擋到滑鼠點擊
            foreach (var g in diagram.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) g.raycastTarget = false;
        }

        // ── 連段狀態 ───────────────────────────────────────────────────
        private void UpdateSnapshot()
        {
            if (Player == null) { _snap = default; return; }

            var state = Player.CurrentStateType;
            var mas = Player.MoveAttackDebug;
            if (state == PlayerStateType.MoveAttack && mas != null && mas.AwaitingHold && (mas.CurrentMove == null || !mas.CurrentMove.IsValid))
            {
                // 從待機直接按 △：還沒出招，正在判斷短按／長按
                _snap = new Snapshot { awaitingHold = true, pendingStrong = mas.PendingStrongIndex };
                _lastActiveTime = Time.unscaledTime;
            }
            else if (state == PlayerStateType.MoveAttack && mas != null && mas.CurrentMove != null && mas.CurrentMove.IsValid)
            {
                var m = mas.CurrentMove;
                _snap = new Snapshot
                {
                    active = true,
                    kind = mas.Kind,
                    isCharge = mas.IsCharge,
                    index = mas.MoveIndex,
                    progress = Mathf.Clamp01(mas.Progress),
                    inputStart = m.inputStartRatio,
                    cancelRatio = Player.EnableAttackCancel ? m.cancelRatio : 1f,
                    clipName = m.clip.name,
                    duration = m.Duration,
                    bufLight = mas.BufferedLight,
                    bufHeavy = mas.BufferedHeavy,
                    bufRoll = mas.BufferedRoll,
                    bufPause = mas.BufferedPause,
                    awaitingHold = mas.AwaitingHold,
                    pendingStrong = mas.PendingStrongIndex,
                    pauseEligible = mas.PauseEligible,
                    windows = m.hitWindows,
                    activeWindow = mas.ActiveHitWindow,
                };
                _lastActiveTime = Time.unscaledTime;
            }
            else if (state == PlayerStateType.SpecialSkill)
            {
                _snap = new Snapshot { special = true };
                _lastActiveTime = Time.unscaledTime;
            }
            else if (Time.unscaledTime - _lastActiveTime > HoldSeconds)
            {
                _snap = default;
            }
            else
            {
                // 剛結束：保留最後一招的路徑，但不再顯示預輸入
                _snap.bufLight = _snap.bufHeavy = _snap.bufRoll = _snap.bufPause = _snap.awaitingHold = false;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  連段進度圖（IMGUI）
        // ════════════════════════════════════════════════════════════
        private void OnGUI()
        {
            if (!Visible || Player == null) return;
            if (PilgrimOfSin.OpeningGuidePanel.IsOpen) return; // 開場引導開著時不蓋在上面
            _s =Mathf.Clamp(Screen.height / 450f, 1f, 4f);
            EnsureStyles();
            LoadTextures();

            float cell = 30f * _s;
            float gap = 16f * _s;          // 格子之間放箭頭的空間
            float labelW = 50f * _s;
            float rowGap = 18f * _s;       // 上下排之間（斜線箭頭、時間文字）
            float pad = 8f * _s;

            float chartW = labelW + 4 * cell + 3 * gap;
            float panelW = chartW + pad * 2;
            float infoH = 36f * _s;
            float chartH = 4 * cell + 3 * rowGap + 10f * _s;
            float panelH = infoH + chartH + pad * 2;

            float x0 = Screen.width - panelW - 10f * _s;
            float y0 = 10f * _s;

            var set = Player.CurrentComboSet;
            int weapon = Player.Combat != null ? Player.Combat.CurrentWeaponIndex : 1;

            GUI.Box(new Rect(x0, y0, panelW, 22f * _s), $"連段進度  [{WeaponName(weapon)}]   F1 切換", _title);
            y0 += 24f * _s;
            GUI.Box(new Rect(x0, y0, panelW, panelH), "", _box);

            float x = x0 + pad, y = y0 + pad;

            if (set == null)
            {
                GUI.Label(new Rect(x, y, chartW, 40f * _s), "這把武器沒有勾選「使用出招表」\n（地面攻擊使用舊連段）", _label);
                return;
            }

            // ── 資訊列 ──
            GUI.Label(new Rect(x, y, chartW, 18f * _s), InfoLine1(), _label);
            GUI.Label(new Rect(x, y + 17f * _s, chartW, 18f * _s), InfoLine2(), _small);
            y += infoH;

            float gridX = x + labelW;
            Rect CellRect(int col, int row) => new Rect(gridX + col * (cell + gap), y + row * (cell + rowGap), cell, cell);

            // ── 列標題 ──
            GUI.Label(new Rect(x, y, labelW, cell), "一般攻擊", _label);
            GUI.Label(new Rect(x, y + cell + rowGap, labelW, cell), "強攻擊", _label);
            GUI.Label(new Rect(x, y + 2 * (cell + rowGap), labelW, cell), "惡魔獵人", _label);
            GUI.Label(new Rect(x, y + 3 * (cell + rowGap), labelW, cell), "秘劍", _label);

            // ── 路徑：哪些格子已經打過 ──
            int litNormals = -1;      // 已打到第幾個 N（含）
            int currentN = -1, currentC = -1;
            bool currentPause = false, currentHold = false;
            if (_snap.active)
            {
                switch (_snap.kind)
                {
                    case MoveAttackState.MoveKind.Strong: currentC = _snap.index; litNormals = _snap.index - 1; break;
                    case MoveAttackState.MoveKind.Normal: currentN = _snap.index; litNormals = _snap.index; break;
                    case MoveAttackState.MoveKind.Pause: currentPause = true; litNormals = set.pauseAfterNormal - 1; break;
                    case MoveAttackState.MoveKind.Hold: currentHold = true; break;
                }
            }
            int bufN = -1, bufC = -1;
            if (_snap.active && _snap.bufLight)
                bufN = (_snap.kind == MoveAttackState.MoveKind.Normal && _snap.index + 1 < ComboMoveSet.Count && Valid(set.GetNormal(_snap.index + 1))) ? _snap.index + 1 : 0;
            if (_snap.active && _snap.bufHeavy)
            {
                int c = _snap.kind == MoveAttackState.MoveKind.Normal ? _snap.index + 1 : 0;
                bufC = c < ComboMoveSet.Count && Valid(set.GetCharge(c)) ? c : 0;
            }
            // △ 判斷短按／長按中：短按會出的 C 和蓄力兩格一起閃
            if (_snap.awaitingHold) bufC = Valid(set.GetCharge(_snap.pendingStrong)) ? _snap.pendingStrong : 0;
            bool bufPause = _snap.active && _snap.bufPause;
            bool bufHold = _snap.awaitingHold;

            // ── 箭頭（先畫，格子蓋在上面） ──
            for (int i = 0; i < ComboMoveSet.Count - 1; i++)
            {
                var a = CellRect(i, 0); var b = CellRect(i + 1, 0);
                bool on = litNormals >= i + 1;
                Arrow(new Vector2(a.xMax + 2f * _s, a.center.y), new Vector2(b.xMin - 2f * _s, b.center.y), on ? Lit : Dim);
            }
            for (int k = 1; k < ComboMoveSet.Count; k++)
            {
                var a = CellRect(k - 1, 0); var b = CellRect(k, 1);
                bool on = currentC == k;
                Arrow(new Vector2(a.xMax - 4f * _s, a.yMax + 2f * _s), new Vector2(b.xMin + 4f * _s, b.yMin - 2f * _s), on ? Current : Dim);
            }

            // ── 格子 ──
            for (int i = 0; i < ComboMoveSet.Count; i++)
            {
                var n = set.GetNormal(i);
                DrawCell(CellRect(i, 0), $"N{i + 1}", n, _squareIdle, _squareLit, "⬜",
                         lit: i <= litNormals && i != currentN, current: i == currentN, buffered: i == bufN);

                var c = set.GetCharge(i);
                DrawCell(CellRect(i, 1), $"C{i + 1}", c, _triangleIdle, _triangleLit, "△",
                         lit: false, current: i == currentC, buffered: i == bufC);
            }

            // 惡魔獵人分支：停頓連打、蓄力
            var pauseCell = CellRect(0, 2);
            var holdCell = CellRect(1, 2);
            DrawCell(pauseCell, "停頓", set.pauseCombo, _squareIdle, _squareLit, "⬜",
                     lit: false, current: currentPause, buffered: bufPause);
            DrawCell(holdCell, "蓄力", set.chargeHold, _triangleIdle, _triangleLit, "△",
                     lit: false, current: currentHold, buffered: bufHold);
            string branchHint = Valid(set.pauseCombo) ? $"停頓 = {new string('⬜', set.pauseAfterNormal)}（停）⬜" : "停頓：沒放動畫";
            branchHint += Valid(set.chargeHold) ? $"\n蓄力 = △ 按住 {set.chargeHoldSeconds:F2}s" : "\n蓄力：沒放動畫";
            GUI.Label(new Rect(holdCell.xMax + gap, holdCell.y, 2 * (cell + gap), cell), branchHint, _small);

            // 秘劍
            var sp = CellRect(0, 3);
            DrawCell(sp, "SP", null, _r1Idle, _r1Lit, "R1", lit: false, current: _snap.special, buffered: false, alwaysValid: true);
            GUI.Label(new Rect(sp.xMax + 8f * _s, sp.y, 3 * (cell + gap), cell),
                      Player.CanUseSpecial ? "特殊招式（可用）" : "特殊招式（冷卻中）", _label);
        }

        private string InfoLine1()
        {
            if (_snap.special)
            {
                var sp = Player.SpecialSkillDebug;
                if (sp == null || sp.HitCount == 0) return "秘劍（特殊招式）施放中　整招一下　霸體";
                string hit = sp.ActiveHitWindow >= 0 ? $"命中 第{sp.ActiveHitWindow + 1}/{sp.HitCount}下" : $"命中 {sp.HitCount} 下";
                return $"秘劍（特殊招式）施放中　{sp.Progress * 100f:F0}%　{hit}　霸體";
            }
            if (_snap.awaitingHold && !_snap.active) return $"△ 判斷中：放開 = C{_snap.pendingStrong + 1}，按住 = 蓄力";
            if (!_snap.active) return "待機中：按 ⬜ / △ 出招";
            string slot = _snap.kind switch
            {
                MoveAttackState.MoveKind.Normal => "N" + (_snap.index + 1),
                MoveAttackState.MoveKind.Strong => "C" + (_snap.index + 1),
                MoveAttackState.MoveKind.Pause => "停頓連打",
                _ => "蓄力",
            };
            return $"{slot}  {_snap.clipName}";
        }

        private string InfoLine2()
        {
            if (!_snap.active) return "";
            float t = _snap.progress * _snap.duration;
            string buf = _snap.awaitingHold ? $"△ 判斷中（放開 C{_snap.pendingStrong + 1}／按住 蓄力）"
                       : _snap.bufRoll ? "翻滾" : _snap.bufPause ? "停頓連打" : _snap.bufHeavy ? "△" : _snap.bufLight ? "⬜" : "—";
            string phase = _snap.progress < _snap.inputStart ? "未開放"
                         : _snap.progress < _snap.cancelRatio ? "接收中"
                         : _snap.progress >= 1f && _snap.pauseEligible ? "停頓寬限：⬜ = 停頓連打"
                         : _snap.pauseEligible ? "可接招：⬜ = 停頓連打"
                         : "可接招";
            int hitCount = _snap.windows != null ? _snap.windows.Count : 0;
            string hit = hitCount == 0 ? "整招一下"
                       : _snap.activeWindow >= 0 ? $"命中 第{_snap.activeWindow + 1}/{hitCount}下"
                       : $"命中 {hitCount} 下";
            return $"{t:F2} / {_snap.duration:F2}s   接收 {_snap.inputStart * 100f:F0}% · 取消 {_snap.cancelRatio * 100f:F0}%（{phase}）   {hit}   預輸入：{buf}";
        }

        private static bool Valid(ComboMoveEntry m) => m != null && m.IsValid;

        private void DrawCell(Rect r, string slot, ComboMoveEntry move, Texture2D idle, Texture2D litTex, string fallback,
                              bool lit, bool current, bool buffered, bool alwaysValid = false)
        {
            bool valid = alwaysValid || Valid(move);

            // 外框
            Color frame = current ? Current : buffered ? Buffered : lit ? Lit : new Color(1f, 1f, 1f, valid ? 0.4f : 0.15f);
            float th = (current || buffered ? 2.5f : 1f) * _s;
            if (buffered && !current) frame.a = 0.55f + 0.45f * Mathf.PingPong(Time.unscaledTime * 4f, 1f);
            FillRect(r, new Color(0f, 0f, 0f, 0.55f));
            Outline(r, frame, th);

            // 圖示
            var icon = current || lit ? (litTex != null ? litTex : idle) : idle;
            var inner = new Rect(r.x + r.width * 0.16f, r.y + r.height * 0.16f, r.width * 0.68f, r.height * 0.68f);
            var prev = GUI.color;
            GUI.color = !valid ? new Color(1f, 1f, 1f, 0.15f) : (current || lit) ? Color.white : new Color(1f, 1f, 1f, 0.55f);
            if (icon != null) GUI.DrawTexture(inner, icon, ScaleMode.ScaleToFit);
            else GUI.Label(inner, fallback, _center);
            GUI.color = prev;

            // 格子下方：編號 + 長度（例：N1 0.79s）
            if (move != null)
                GUI.Label(new Rect(r.x - 8f * _s, r.yMax, r.width + 16f * _s, 12f * _s),
                          valid ? $"{slot} {move.Duration:F2}s" : $"{slot} 空", _center);

            // 正在播：進度條 + 取消時間點刻度
            if (current && _snap.active)
            {
                var bar = new Rect(r.x, r.yMax - 4f * _s, r.width, 4f * _s);
                FillRect(bar, new Color(0f, 0f, 0f, 0.8f));
                bool canCancel = _snap.progress >= _snap.cancelRatio;
                FillRect(new Rect(bar.x, bar.y, bar.width * _snap.progress, bar.height),
                         canCancel ? new Color(0.3f, 1f, 0.4f) : Current);
                // 命中區間：進度條上方的橘色小段（正在打的那一下較亮）
                if (_snap.windows != null)
                {
                    for (int i = 0; i < _snap.windows.Count; i++)
                    {
                        var w = _snap.windows[i];
                        var c = i == _snap.activeWindow ? new Color(1f, 0.55f, 0.1f, 1f) : new Color(1f, 0.55f, 0.1f, 0.45f);
                        FillRect(new Rect(bar.x + bar.width * w.start, bar.y - 3f * _s, Mathf.Max(1f, bar.width * (w.end - w.start)), 2f * _s), c);
                    }
                }
                // 黃色刻度 = 接收開始；白色刻度 = 取消時間點
                FillRect(new Rect(bar.x + bar.width * _snap.inputStart - 1f * _s, bar.y - 2f * _s, 2f * _s, bar.height + 4f * _s), Buffered);
                FillRect(new Rect(bar.x + bar.width * _snap.cancelRatio - 1f * _s, bar.y - 2f * _s, 2f * _s, bar.height + 4f * _s), Color.white);
            }
        }

        // ── 繪圖小工具 ─────────────────────────────────────────────────
        private void FillRect(Rect r, Color c)
        {
            var prev = GUI.color; GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = prev;
        }

        private void Outline(Rect r, Color c, float th)
        {
            FillRect(new Rect(r.x, r.y, r.width, th), c);
            FillRect(new Rect(r.x, r.yMax - th, r.width, th), c);
            FillRect(new Rect(r.x, r.y, th, r.height), c);
            FillRect(new Rect(r.xMax - th, r.y, th, r.height), c);
        }

        private void Line(Vector2 a, Vector2 b, Color c, float th)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.5f) return;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, a);
            FillRect(new Rect(a.x, a.y - th * 0.5f, len, th), c);
            GUI.matrix = m;
        }

        private void Arrow(Vector2 a, Vector2 b, Color c)
        {
            float th = 2f * _s;
            Line(a, b, c, th);
            Vector2 dir = (b - a).normalized;
            float head = 5f * _s;
            Vector2 left = (Vector2)(Quaternion.Euler(0, 0, 150f) * dir) * head;
            Vector2 right = (Vector2)(Quaternion.Euler(0, 0, -150f) * dir) * head;
            Line(b, b + left, c, th);
            Line(b, b + right, c, th);
        }

        private void LoadTextures()
        {
            if (_texturesLoaded) return;
            _texturesLoaded = true;
#if UNITY_EDITOR
            Texture2D T(string p) => UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(IconRoot + p);
            _squareIdle = T("按鈕_黑白/右邊圖形鍵-正方形.png");
            _triangleIdle = T("按鈕_黑白/右邊圖形鍵-三角形.png");
            _r1Idle = T("按鈕_黑白/圖示用_R1.png");
            _squareLit = T("按鈕_更新紅色/右邊圖形鍵-正方形_紅.png");
            _triangleLit = T("按鈕_更新紅色/右邊圖形鍵-三角形_紅.png");
            _r1Lit = T("按鈕_更新紅色/圖示用_R1_紅.png");
#endif
        }

        private void EnsureStyles()
        {
            if (_box != null && Mathf.Approximately(_styleScale, _s)) return;
            _styleScale = _s;
            _box = new GUIStyle(GUI.skin.box) { normal = { background = MakeTex(new Color(0f, 0f, 0f, 0.65f)) } };
            _title = new GUIStyle(GUI.skin.box)
            {
                normal = { background = MakeTex(new Color(0.38f, 0.08f, 0.12f, 0.9f)), textColor = Color.white },
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(Mathf.RoundToInt(8f * _s), 0, 0, 0),
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.RoundToInt(12f * _s),
            };
            _label = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = Color.white },
                fontSize = Mathf.RoundToInt(11f * _s),
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
            };
            _small = new GUIStyle(_label) { fontSize = Mathf.RoundToInt(8f * _s), alignment = TextAnchor.UpperLeft, wordWrap = false };
            _center = new GUIStyle(_small) { alignment = TextAnchor.UpperCenter };
        }

        private static Texture2D MakeTex(Color col)
        {
            var tex = new Texture2D(2, 2);
            tex.SetPixels(new[] { col, col, col, col });
            tex.Apply();
            return tex;
        }

        private static string WeaponName(int idx) => idx switch
        {
            1 => "鉛筆", 2 => "水彩筆", 3 => "畫刀", 4 => "調色盤", _ => "?",
        };
    }
}

#endif
