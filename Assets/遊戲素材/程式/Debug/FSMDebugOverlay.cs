#if UNITY_EDITOR || DEVELOPMENT_BUILD

using UnityEngine;
using UnityEngine.InputSystem;

namespace PilgrimOfSin.StateMachine
{
    public class FSMDebugOverlay : MonoBehaviour
    {
        private const float PanelWidth  = 240f;
        private const float CacheInterval = 1.5f;

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
            EnsureStyles();

            float y = 10f;

            if (_player != null)
                y = DrawPlayerPanel(10f, y) + 5f;

            if      (_greed   != null) DrawGreedPanel(10f, y);
            else if (_wrath   != null) DrawWrathPanel(10f, y);
            else if (_foolish != null) DrawFoolishPanel(10f, y);
        }

        // ── Player 面板：State / Weapon / HP ─────────────────────────

        private float DrawPlayerPanel(float x, float y)
        {
            DrawTitleBar(x, y, "Player [Muir]   F1 切換");
            y += 24f;

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
            y += 24f;

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
            y += 24f;

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
            y += 24f;

            float hp    = _foolish.CurrentHp;
            float maxHp = _foolish.MaxHp;

            DrawRows(x, y, new[]
            {
                $"State  : {_foolish.CurrentStateType}",
                $"HP     : {hp:F0} / {maxHp:F0}  ({hp / maxHp:P0})",
                $"Phase  : {_foolish.CurrentPhase}",
                $"Timer  : {_foolishTimer:F1} s",
            });
        }

        // ── 共用繪製輔助 ─────────────────────────────────────────────

        private void DrawTitleBar(float x, float y, string title)
            => GUI.Box(new Rect(x, y, PanelWidth, 22f), title, _titleStyle);

        private float DrawRows(float x, float y, string[] rows)
        {
            float h = rows.Length * 20f + 8f;
            GUI.Box(new Rect(x, y, PanelWidth, h), "", _boxStyle);
            for (int i = 0; i < rows.Length; i++)
                GUI.Label(new Rect(x + 8f, y + 4f + i * 20f, PanelWidth - 16f, 20f), rows[i], _rowStyle);
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
            if (_stylesReady) return;
            _stylesReady = true;

            _boxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = MakeTex(new Color(0f, 0f, 0f, 0.65f)) },
            };

            _titleStyle = new GUIStyle(GUI.skin.box)
            {
                normal   = { background = MakeTex(new Color(0.08f, 0.08f, 0.38f, 0.88f)),
                             textColor  = Color.white },
                alignment = TextAnchor.MiddleLeft,
                padding   = new RectOffset(8, 0, 0, 0),
                fontStyle = FontStyle.Bold,
                fontSize  = 12,
            };

            _rowStyle = new GUIStyle(GUI.skin.label)
            {
                normal  = { textColor = Color.white },
                fontSize = 11,
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
