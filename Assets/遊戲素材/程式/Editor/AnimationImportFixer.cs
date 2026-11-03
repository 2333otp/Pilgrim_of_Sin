#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PilgrimOfSin.StateMachine.EditorTools
{
    /// <summary>
    /// 一鍵修正主角動畫 FBX 的匯入設定，避免換進來的新動畫遁地：
    ///   - 還沒設定過動畫片段（clips 為空，等於全用預設值）的 FBX：建立片段設定並勾 Bake Into Pose。
    ///   - 已設定但沒勾 Root Transform Position (Y) Bake Into Pose 的 FBX：補勾。
    ///   - Jump / Fall 開頭的動畫保留原樣（垂直位移是刻意的）。
    /// 並檢查 Animation Type 是否與玩家模型一致（Humanoid），不一致只警告不更動。
    /// 選單：PilgrimOfSin/主角/修正動畫匯入設定 (Bake Into Pose)
    /// </summary>
    public static class AnimationImportFixer
    {
        private const string AnimationFolder = "Assets/遊戲素材/角色/謬爾/animation";
        private const string ModelPath = "Assets/遊戲素材/角色/謬爾/Muir_new_mat_deform.fbx";

        [MenuItem("PilgrimOfSin/主角/修正動畫匯入設定 (Bake Into Pose)")]
        public static void Fix()
        {
            var modelImporter = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            var modelType = modelImporter != null ? modelImporter.animationType : ModelImporterAnimationType.Human;

            var report = new StringBuilder();
            var changed = new List<string>();

            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { AnimationFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;
                string fileName = Path.GetFileNameWithoutExtension(path);

                if (importer.animationType != modelType)
                    report.AppendLine($"✗ {fileName}：Animation Type 是 {importer.animationType}，玩家模型是 {modelType}，動畫可能無法套用。");

                // Jump / Fall 的垂直位移是刻意的，不動
                if (fileName.Contains("Jump") || fileName.Contains("Fall")) continue;

                bool unconfigured = importer.clipAnimations == null || importer.clipAnimations.Length == 0;
                var clips = unconfigured ? importer.defaultClipAnimations : importer.clipAnimations;
                if (clips == null || clips.Length == 0) continue;

                bool dirty = unconfigured;
                foreach (var c in clips)
                {
                    if (!c.lockRootHeightY) { c.lockRootHeightY = true; dirty = true; }
                    if (unconfigured)
                    {
                        // 新動畫沒有攻擊類的根位移需求：方向、水平位置也一併烘進姿勢，由程式控制位移
                        c.lockRootRotation = true;
                        c.lockRootPositionXZ = true;
                    }
                }
                if (!dirty) continue;

                importer.clipAnimations = clips;
                importer.SaveAndReimport();
                changed.Add(fileName);
            }

            string summary = $"[AnimationImportFixer] 玩家模型 Animation Type：{modelType}。已修正 {changed.Count} 個 FBX。\n" +
                             (changed.Count > 0 ? "已修正：" + string.Join("、", changed) + "\n" : "") +
                             (report.Length > 0 ? report.ToString() : "沒有發現 Animation Type 不一致。");
            Debug.Log(summary);
        }
    }
}
#endif
