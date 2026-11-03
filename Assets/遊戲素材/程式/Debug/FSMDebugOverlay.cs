#if UNITY_EDITOR || DEVELOPMENT_BUILD

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace PilgrimOfSin.StateMachine
{
    public class FSMDebugOverlay : MonoBehaviour
    {
        // 只在這些戰鬥場景自動建立，其餘場景（HubScene、MainScene 等）預設不顯示
        private static readonly string[] AllowedScenes =
        {
            "GreedBossScene",
            "WrathBossScene",
            "FoolishBossScene",
        };

        private const float PanelWidth  = 300f;
        private const float CacheInterval = 1.5f;

        // 版面依畫面高度縮放：Game 視窗解析度越高，面板越大（450px 高 = 1 倍，限制 1~4 倍）
        private float _scale = 1f;
        private float _styleScale = -1f;
        private float PanelW => PanelWidth * _scale;
        private float TitleBarStep => 24f * _scale;
        private float RowH => 20f * _scale;

        private bool _visible = true;

        // 快取的 Controller 參照（每 1.5 秒刷新一次）
        private PlayerController      _player;
        private GreedBossController   _greed;
        private WrathBossController   _wrath;
        private FoolishBossController _foolish;
        private float _cacheTimer;

        // Boss 狀態計時器（在 Overlay 內部追蹤，不修改狀態機）
        private GreedBossStateType   _lastGreedState;
        private WrathBossStateType   _lastWrathState;
        private FoolishBossStateType _lastFoolishState;
        private float _greedTimer, _wrathTimer, _foolishTimer;

        // IMGUI 樣式（首次 OnGUI 時建立）
        private GUIStyle _boxStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _rowStyle;
        private bool _stylesReady;

        // ── 自動建立（不需要手動放 GameObject）──────────────────────
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (System.Array.IndexOf(AllowedScenes, SceneManager.GetActiveScene().name) < 0) return;
            if (FindAnyObjectByType<FSMDebugOverlay>() != null) return;
            var go = new GameObject("[FSM Debug Overlay]");
            DontDestroyOnLoad(go);
            go.AddComponent<FSMDebugOverlay>();
        }

        private void Awake()
        {
            // 防止跨場景重複建立
            if (FindObjectsByType<FSMDebugOverlay>(FindObjectsSortMode.None).Length > 1)
            {
                Destroy(gameObject);
                return;
            }
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        // 離開白名單場景（例如打完 Boss 回到 HubScene）時自我銷毀
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (System.Array.IndexOf(AllowedScenes, scene.name) < 0)
                Destroy(gameObject);
        }

        // ── Update ───────────────────────────────────────────────────

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
                _visible = !_visible;

            _cacheTimer -= Time.unscaledDeltaTime;
            if (_cacheTimer <= 0f)
            {
                RefreshRefs();
                _cacheTimer = CacheInterval;
            }

            UpdateBossTimers();
        }

        private void RefreshRefs()
        {
            _player  = FindAnyObjectByType<PlayerController>();
            _greed   = FindAnyObjectByType<GreedBossController>();
            _wrath   = FindAnyObjectByType<WrathBossController>();
            _foolish = FindAnyObjectByType<FoolishBossController>();
        }

        private void UpdateBossTimers()
        {
            float dt = Time.unscaledDeltaTime;

            if (_greed != null)
            {
                var s = _greed.CurrentStateType;
                if (s != _lastGreedState) { _lastGreedState = s; _greedTimer = 0f; }
                else _greedTimer += dt;
            }

            if (_wrath != null)
            {
                var s = _wrath.CurrentState;
                if (s != _lastWrathState) { _lastWrathState = s; _wrathTimer = 0f; }
                else _wrathTimer += dt;
            }

            if (_foolish != null)
            {
                var s = _foolish.CurrentStateType;
                if (s != _lastFoolishState) { _lastFoolishState = s; _foolishTimer = 0f; }
                else _foolishTimer += dt;
            }
        }

        // ── OnGUI ────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!_visible) return;

            _scale = Mathf.Clamp(Screen.height / 450f, 1f, 4f);
            EnsureStyles();

            float x = 10f * _scale;
            float y = 10f * _scale;

            if (_player != null)
                y = DrawPlayerPanel(x, y) + 5f * _scale;

            if      (_greed   != null) DrawGreedPanel(x, y);
            else if (_wrath   != null) DrawWrathPanel(x, y);
            else if (_foolish != null) DrawFoolishPanel(x, y);
        }

        // ── Player 面板：State / Weapon / HP ─────────────────────────

        private float DrawPlayerPanel(float x, float y)
        {
            DrawTitleBar(x, y, "Player [Muir]   F1 切換");
            y += TitleBarStep;

            float hp    = _player.CurrentHp;
            float maxHp = _player.MaxHp;

            string[] rows;
            if (_player.EnableWeaponSwitch)
            {
                int weaponIdx = _player.Combat != null ? _player.Combat.CurrentWeaponIndex : 0;
                rows = new[]
                {
                    $"State  : {_player.CurrentStateType}",
                    $"Weapon : {WeaponName(weaponIdx)}",
                    $"HP     : {hp:F0} / {maxHp:F0}  ({hp / maxHp:P0})",
                };
            }
            else
            {
                rows = new[]
                {
                    $"State  : {_player.CurrentStateType}",
                    $"HP     : {hp:F0} / {maxHp:F0}  ({hp / maxHp:P0})",
                };
            }

            return DrawRows(x, y, rows);
        }

        // ── 貪 面板：State / HP / ScalePhase / Timer ─────────────────

        private void DrawGreedPanel(float x, float y)
        {
            DrawTitleBar(x, y, "Boss [貪]");
            y += TitleBarStep;

            float hp    = _greed.CurrentHp;
            float maxHp = _greed.MaxHp;

            DrawRows(x, y, new[]
            {
                $"State  : {_greed.CurrentStateType}",
                $"HP     : {hp:F0} / {maxHp:F0}  ({hp / maxHp:P0})",
                $"Phase  : {_greed.CurrentPhase}",
                $"Timer  : {_greedTimer:F1} s",
            });
        }

        // ── 嗔 面板：State / HP / Timer（無場地相位）────────────────

        private void DrawWrathPanel(float x, float y)
        {
            DrawTitleBar(x, y, "Boss [嗔]");
            y += TitleBarStep;

            float hp    = _wrath.CurrentHp;
            float maxHp = _wrath.MaxHp;

            DrawRows(x, y, new[]
            {
                $"State  : {_wrath.CurrentState}",
                $"HP     : {hp:F0} / {maxHp:F0}  ({hp / maxHp:P0})",
                $"Timer  : {_wrathTimer:F1} s",
            });
        }

        // ── 癡 面板：State / HP / FoolishPhase / Timer ───────────────

        private void DrawFoolishPanel(float x, float y)
        {
            DrawTitleBar(x, y, "Boss [癡]");
            y += TitleBarStep;

            float hp    = _foolish.CurrentHp;
            float maxHp = _foolish.MaxHp;

            DrawRows(x, y, new[]
            {
                $"State  : {_foolish.CurrentStateType}",
                $"HP     : {hp:F0} / {maxHp:F0}  ({hp / maxHp:P0})",
                $"Phase  : {_foolish.CurrentPhase}  /  {_foolish.CyclePhase}",
                $"Left   : {_foolish.PhaseTimeRemaining:F1} s",
                $"Safe   : {_foolish.ActiveSafeZoneCount} 個{(_foolish.SafeZonesArePreview ? "（預告）" : "")}   玩家在善區：{(_foolish.PlayerInSafeZone ? "是" : "否")}",
                $"Evil   : {(_foolish.EvilZoneDamaging ? "生效" : "無")}  {_foolish.CurrentEvilDps:F0}/s",
            });
        }

        // ── 共用繪製輔助 ─────────────────────────────────────────────

        private void DrawTitleBar(float x, float y, string title)
            => GUI.Box(new Rect(x, y, PanelW, 22f * _scale), title, _titleStyle);

        private float DrawRows(float x, float y, string[] rows)
        {
            float h = rows.Length * RowH + 8f * _scale;
            GUI.Box(new Rect(x, y, PanelW, h), "", _boxStyle);
            for (int i = 0; i < rows.Length; i++)
                GUI.Label(new Rect(x + 8f * _scale, y + 4f * _scale + i * RowH, PanelW - 16f * _scale, RowH), rows[i], _rowStyle);
            return y + h;
        }

        // ── 武器索引轉名稱 ───────────────────────────────────────────

        private static string WeaponName(int idx) => idx switch
        {
            1 => "1 鉛筆",
            2 => "2 水彩筆",
            3 => "3 畫刀",
            4 => "4 調色盤",
            _ => $"?({idx})",
        };

        // ── IMGUI 樣式初始化 ─────────────────────────────────────────

        private void EnsureStyles()
        {
            // 縮放倍率改變（例如調整 Game 視窗大小）時重建字級
            if (_stylesReady && Mathf.Approximately(_styleScale, _scale)) return;
            _stylesReady = true;
            _styleScale = _scale;

            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = MakeTex(new Color(0f, 0f, 0f, 0.65f)) },
            };

            _titleStyle = new GUIStyle(GUI.skin.box)
            {
                normal   = { background = MakeTex(new Color(0.08f, 0.08f, 0.38f, 0.88f)),
                             textColor  = Color.white },
                alignment = TextAnchor.MiddleLeft,
                padding   = new RectOffset(Mathf.RoundToInt(8f * _scale), 0, 0, 0),
                fontStyle = FontStyle.Bold,
                fontSize  = Mathf.RoundToInt(12f * _scale),
            };

            _rowStyle = new GUIStyle(GUI.skin.label)
            {
                normal  = { textColor = Color.white },
                fontSize = Mathf.RoundToInt(11f * _scale),
            };
        }

        private static Texture2D MakeTex(Color col)
        {
            var tex = new Texture2D(2, 2);
            var px  = new Color[] { col, col, col, col };
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }
}

#endif
