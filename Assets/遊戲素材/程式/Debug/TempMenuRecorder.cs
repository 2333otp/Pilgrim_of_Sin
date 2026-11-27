#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace PilgrimOfSin.DebugTools
{
    /// <summary>
    /// 【暫時工具，錄完提報影片就刪】選單錄影：用虛擬手把照腳本操作選單，並逐格存下 Game 畫面。
    ///
    /// - Time.captureDeltaTime 固定每格的遊戲時間 → 存圖再慢也不會掉格，動畫速度正確
    /// - 錄影期間停用滑鼠，避免游標 hover 搶走手把的選取
    /// - 腳本格式（以 | 分隔）：
    ///     wait:N           等 N 格
    ///     press:Button     按一下（按住 2 格後放開），Button = GamepadButton 名稱，如 DpadDown、East、South、Start
    ///     mark:文字         在 log 記一個標記（之後剪輯/除錯用）
    ///     rec:on / rec:off  開始／暫停存圖（暫停時操作照跑，用來快轉跳過不需要的過程）
    /// </summary>
    public class TempMenuRecorder : MonoBehaviour
    {
        public static bool Done { get; private set; }
        public static string LastLog { get; private set; } = "";

        private string _outDir;
        private string _script;
        private int _fps;
        private Gamepad _pad;
        private int _saved;
        private bool _rec = true;
        private readonly StringBuilder _log = new StringBuilder();

        public static void Begin(string outDir, string script, int fps)
        {
            Done = false;
            var go = new GameObject("_TempMenuRecorder");
            DontDestroyOnLoad(go);
            var r = go.AddComponent<TempMenuRecorder>();
            r._outDir = outDir; r._script = script; r._fps = fps;
            Directory.CreateDirectory(outDir);
            r.StartCoroutine(r.Run());
        }

        private IEnumerator Run()
        {
            Time.captureDeltaTime = 1f / _fps;
            if (Mouse.current != null) InputSystem.DisableDevice(Mouse.current);
            _pad = InputSystem.AddDevice<Gamepad>("RecorderPad");
            _pad.MakeCurrent();
            HideDevOverlays();
            yield return null;
            yield return null;

            foreach (var raw in _script.Split('|'))
            {
                var step = raw.Trim();
                if (step.Length == 0) continue;
                int c = step.IndexOf(':');
                string cmd = c < 0 ? step : step.Substring(0, c);
                string arg = c < 0 ? "" : step.Substring(c + 1);

                switch (cmd)
                {
                    case "wait":
                        for (int i = 0; i < int.Parse(arg); i++) yield return Frame();
                        break;
                    case "press":
                        var btn = (GamepadButton)System.Enum.Parse(typeof(GamepadButton), arg);
                        InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(btn));
                        yield return Frame();
                        yield return Frame();
                        InputSystem.QueueStateEvent(_pad, new GamepadState());
                        yield return Frame();
                        break;
                    case "tap": // 只按住 1 格（主畫面是「按住連續移動」，按 2 格會跳兩下）
                        var tbtn = (GamepadButton)System.Enum.Parse(typeof(GamepadButton), arg);
                        InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(tbtn));
                        yield return Frame();
                        InputSystem.QueueStateEvent(_pad, new GamepadState());
                        yield return Frame();
                        break;
                    case "mark":
                        _log.AppendLine($"[{_saved}] {arg} | {DescribeMenu()}");
                        break;
                    case "rec":
                        _rec = arg == "on";
                        break;
                }
            }

            // 收尾：恢復時間、滑鼠，移除虛擬手把
            Time.captureDeltaTime = 0f;
            if (Mouse.current != null) InputSystem.EnableDevice(Mouse.current);
            InputSystem.RemoveDevice(_pad);
            LastLog = _log.ToString() + $"total frames={_saved}";
            File.WriteAllText(Path.Combine(_outDir, "log.txt"), LastLog);
            Done = true;
            Destroy(gameObject);
        }

        /// <summary>等到這格畫完再存圖，然後進下一格。</summary>
        private IEnumerator Frame()
        {
            yield return new WaitForEndOfFrame();
            if (_rec)
            {
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(_outDir, $"f_{_saved:0000}.png"), tex.EncodeToPNG());
                Destroy(tex);
                _saved++;
            }
            yield return null;
        }

        /// <summary>
        /// 關掉開發用除錯畫面（FSM 狀態、連段面板、手把圖、攻擊判定文字）。
        /// 三者都由 FSMDebugOverlay._visible 統一控制（平常按 F1 切換的那個）。
        /// </summary>
        private static void HideDevOverlays()
        {
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (mb == null || mb.GetType().Name != "FSMDebugOverlay") continue;
                mb.GetType().GetField("_visible", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(mb, false);
            }
        }

        /// <summary>記錄目前選單狀態（哪個面板開著、反白哪顆），方便確認腳本有照預期走。</summary>
        private static string DescribeMenu()
        {
            var ui = FindFirstObjectByType<PauseMenuUI>();
            if (ui == null)
            {
                var mm = FindFirstObjectByType<MainMenuUI>();
                if (mm == null) return "no menu";
                var hb = typeof(MainMenuUI).GetField("_hoveredButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(mm) as Component;
                return "main hover=" + (hb ? hb.name : "-");
            }
            var t = typeof(PauseMenuUI);
            var f = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var sub = t.GetField("_currentSubPanel", f)?.GetValue(ui) as GameObject;
            var tab = t.GetField("_currentStatusTab", f)?.GetValue(ui) as GameObject;
            var hov = t.GetField("_hoveredButton", f)?.GetValue(ui) as Component;
            var esc = t.GetField("_escPanel", f)?.GetValue(ui) as GameObject;
            var st = t.GetField("_playerStatusPanel", f)?.GetValue(ui) as GameObject;
            return $"esc={(esc && esc.activeSelf)} status={(st && st.activeSelf)} sub={(sub ? sub.name : "-")} tab={(tab ? tab.name : "-")} hover={(hov ? hov.name : "-")}";
        }
    }
}
#endif
