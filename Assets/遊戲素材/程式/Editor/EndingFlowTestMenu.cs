using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PilgrimOfSin.EditorTools
{
    /// <summary>
    /// 結局流程測試捷徑（只在 Editor 出現，不會進 Build）。
    ///
    /// 用法：
    ///   完整流程：開任一 Boss 場景 → Play → 選「立刻全通關」
    ///             → 通關通知 → 全通關圖 → 結尾動畫 → 跑馬燈 → 主選單
    ///   只看動畫：選「直接播放結尾動畫」（結尾動畫 → 跑馬燈）
    ///   測完記得：停止 Play → 選「還原測試前的存檔」
    ///
    /// 「立刻打贏」會照正常通關流程把進度寫進真正的存檔（save.json），
    /// 所以第一次執行前會自動備份成 save.test_backup.json，還原時再搬回去。
    /// </summary>
    public static class EndingFlowTestMenu
    {
        private const string Root = "PilgrimOfSin/測試/結局流程/";

        private static string SavePath   => Path.Combine(Application.persistentDataPath, "save.json");
        private static string BackupPath => Path.Combine(Application.persistentDataPath, "save.test_backup.json");
        // 測試前根本沒有存檔時，用這個標記檔記住「還原 = 刪掉存檔」
        private static string NoSaveMarker => Path.Combine(Application.persistentDataPath, "save.test_backup.none");

        // ── 1. 立刻全通關（完整結局流程） ──────────────────────────────

        [MenuItem(Root + "立刻全通關（打贏目前 Boss，看完整結局流程）", priority = 0)]
        private static void WinAndCompleteAll()
        {
            if (!TryGetPortal(out var portal)) return;
            BackupSaveOnce();

            var gp = GameProgressManager.Instance;
            gp.MarkBossDefeated(SceneTransitionManager.BossType.Greed);
            gp.MarkBossDefeated(SceneTransitionManager.BossType.Wrath);
            gp.MarkBossDefeated(SceneTransitionManager.BossType.Foolish);

            portal.OnBossDefeated(); // 走正式通關流程（會存檔、播通知、切過場圖…）
            Debug.Log("[結局測試] 已標記三關全通關並觸發打贏，接下來會依序播：通關通知 → 全通關圖 → 結尾動畫 → 跑馬燈。");
        }

        [MenuItem(Root + "立刻全通關（打贏目前 Boss，看完整結局流程）", true)]
        private static bool WinAndCompleteAllValidate() => EditorApplication.isPlaying;

        // ── 2. 只打贏目前 Boss（不動其他關進度） ───────────────────────

        [MenuItem(Root + "立刻打贏目前 Boss（其他關進度不變）", priority = 1)]
        private static void WinCurrentOnly()
        {
            if (!TryGetPortal(out var portal)) return;
            BackupSaveOnce();
            portal.OnBossDefeated();
            Debug.Log("[結局測試] 已觸發打贏目前 Boss。");
        }

        [MenuItem(Root + "立刻打贏目前 Boss（其他關進度不變）", true)]
        private static bool WinCurrentOnlyValidate() => EditorApplication.isPlaying;

        // ── 3. 直接播放結尾動畫 ────────────────────────────────────────

        [MenuItem(Root + "直接播放結尾動畫（不動存檔）", priority = 20)]
        private static void PlayEndingCutscene()
        {
            if (EditorApplication.isPlaying)
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(SceneTransitionManager.ENDING_CUTSCENE_SCENE);
                return;
            }
            // 用 playModeStartScene 讓這次 Play 從結尾動畫開始：不用關掉/切換目前開著的場景，
            // 所以不會跳「要不要存檔」的視窗，目前場景沒存的修改也原封不動。
            var endingScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/EndingCutsceneScene.unity");
            if (endingScene == null) { Debug.LogError("[結局測試] 找不到 EndingCutsceneScene.unity"); return; }
            EditorSceneManager.playModeStartScene = endingScene;
            SessionState.SetBool(ResetStartSceneKey, true);
            EditorApplication.isPlaying = true;
        }

        // 進 Play 會重新載入程式（Domain Reload），臨時掛的事件會消失，
        // 所以用 InitializeOnLoad 常駐監聽＋SessionState 旗標，確保只影響這一次 Play。
        private const string ResetStartSceneKey = "PilgrimOfSin.EndingTest.ResetStartScene";

        [InitializeOnLoadMethod]
        private static void RegisterStartSceneReset()
        {
            EditorApplication.playModeStateChanged += change =>
            {
                if (change != PlayModeStateChange.EnteredPlayMode) return;
                if (!SessionState.GetBool(ResetStartSceneKey, false)) return;
                EditorSceneManager.playModeStartScene = null; // 之後按 Play 恢復從目前場景開始
                SessionState.EraseBool(ResetStartSceneKey);
            };
        }

        // ── 4. 還原存檔 ────────────────────────────────────────────────

        [MenuItem(Root + "還原測試前的存檔", priority = 40)]
        private static void RestoreSave()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("結局測試", "請先停止 Play 再還原（Play 中遊戲可能又把進度寫回存檔）。", "好");
                return;
            }

            if (File.Exists(NoSaveMarker))
            {
                if (File.Exists(SavePath)) File.Delete(SavePath);
                File.Delete(NoSaveMarker);
                Debug.Log("[結局測試] 測試前沒有存檔，已刪除測試產生的存檔。");
            }
            else if (File.Exists(BackupPath))
            {
                File.Copy(BackupPath, SavePath, true);
                File.Delete(BackupPath);
                Debug.Log("[結局測試] 已還原測試前的存檔。");
            }
            else
            {
                EditorUtility.DisplayDialog("結局測試", "沒有找到測試備份，存檔沒有被測試工具動過。", "好");
            }
        }

        [MenuItem(Root + "還原測試前的存檔", true)]
        private static bool RestoreSaveValidate() => File.Exists(BackupPath) || File.Exists(NoSaveMarker);

        // ── 共用 ──────────────────────────────────────────────────────

        private static bool TryGetPortal(out BossResultPortal portal)
        {
            portal = BossResultPortal.Instance;
            if (portal == null || GameProgressManager.Instance == null)
            {
                EditorUtility.DisplayDialog("結局測試",
                    "請在 Boss 場景（貪／嗔／癡）的 Play 中使用。\n目前場景找不到 BossResultPortal 或 GameProgressManager。", "好");
                return false;
            }

            // 進關時的操作引導會把遊戲暫停（timeScale=0），開著的話通關流程的等待會永遠卡住 → 先幫忙關掉
            if (OpeningGuidePanel.IsOpen && OpeningGuidePanel.Instance != null)
            {
                typeof(OpeningGuidePanel)
                    .GetMethod("Close", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.Invoke(OpeningGuidePanel.Instance, null);
                Debug.Log("[結局測試] 已自動關閉開場操作引導。");
            }

            if (Time.timeScale == 0f)
            {
                EditorUtility.DisplayDialog("結局測試",
                    "遊戲目前是暫停狀態（例如 ESC 選單開著），通關流程會卡住。\n請先回到戰鬥畫面再按一次。", "好");
                return false;
            }
            return true;
        }

        /// <summary>只在第一次備份，連續測好幾次也不會把「測試中的存檔」蓋掉真正的備份。</summary>
        private static void BackupSaveOnce()
        {
            if (File.Exists(BackupPath) || File.Exists(NoSaveMarker)) return;
            if (File.Exists(SavePath)) File.Copy(SavePath, BackupPath);
            else File.WriteAllText(NoSaveMarker, "");
        }
    }
}
