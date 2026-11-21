#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PilgrimOfSin.StateMachine.EditorTools
{
    /// <summary>
    /// 把一整段的連段動畫切成一下一下的短動畫（給「無雙式」普攻鏈／蓄力攻擊研究用）。
    ///
    /// 做法：照 ComboSliceTable 填的影格範圍，把原動畫每一條曲線（Humanoid 肌肉、RootT/RootQ、
    ///       頭髮袖子等非人形骨頭）逐格取樣，搬到新的 .anim，時間從 0 開始。Root Transform 設定
    ///       沿用原動畫（攻擊類保留水平根位移）。原動畫、Animator、WeaponActions 一律不動。
    /// 輸出：Assets/遊戲素材/角色/謬爾/animation/Sliced/&lt;武器&gt;/&lt;段名&gt;.anim
    /// 選單：PilgrimOfSin/主角/連段切割/建立預設切割表、產生全部切割動畫
    /// </summary>
    public static class ComboClipSlicer
    {
        public const string TablePath = "Assets/遊戲素材/SO/主角/ComboSliceTable.asset";
        private const string AnimRoot = "Assets/遊戲素材/角色/謬爾/animation";
        private const string OutputRoot = AnimRoot + "/Sliced";

        // ════════════════════════════════════════════════════════════
        //  選單
        // ════════════════════════════════════════════════════════════

        [MenuItem("PilgrimOfSin/主角/連段切割/建立預設切割表")]
        public static void CreateDefaultTableMenu()
        {
            var existing = AssetDatabase.LoadAssetAtPath<ComboSliceTable>(TablePath);
            if (existing != null &&
                !EditorUtility.DisplayDialog("連段切割表", "切割表已存在，要用預設值覆蓋嗎？（手動調過的影格會被蓋掉）", "覆蓋", "取消"))
            {
                Selection.activeObject = existing;
                return;
            }
            var table = CreateOrResetDefaultTable();
            Selection.activeObject = table;
        }

        [MenuItem("PilgrimOfSin/主角/連段切割/產生全部切割動畫")]
        public static void SliceAllMenu()
        {
            var table = AssetDatabase.LoadAssetAtPath<ComboSliceTable>(TablePath);
            if (table == null)
            {
                Debug.LogError($"[ComboClipSlicer] 找不到切割表：{TablePath}，請先執行「建立預設切割表」。");
                return;
            }
            Debug.Log(SliceAll(table));
        }

        // ════════════════════════════════════════════════════════════
        //  切割
        // ════════════════════════════════════════════════════════════

        /// <summary>依切割表產生全部切割動畫，回傳報告文字。</summary>
        public static string SliceAll(ComboSliceTable table)
        {
            var report = new StringBuilder("[ComboClipSlicer] 切割完成：\n");
            int count = 0;
            var produced = new HashSet<string>();
            var weaponFolders = new HashSet<string>();
            foreach (var src in table.sources)
                weaponFolders.Add($"{OutputRoot}/{(string.IsNullOrEmpty(src.weapon) ? "Misc" : src.weapon)}");

            foreach (var src in table.sources)
            {
                if (!src.enabled) continue;
                if (src.source == null) { report.AppendLine($"✗ {src.label}：沒有指定原始動畫"); continue; }

                string folder = EnsureFolder(src.weapon);
                float fps = src.source.frameRate > 0f ? src.source.frameRate : 24f;
                int lastFrame = Mathf.RoundToInt(src.source.length * fps);

                foreach (var seg in src.segments)
                {
                    int a = Mathf.Clamp(seg.startFrame, 0, lastFrame);
                    int b = Mathf.Clamp(seg.endFrame, 0, lastFrame);
                    if (b <= a) { report.AppendLine($"✗ {src.label} / {seg.name}：影格範圍無效（{seg.startFrame}~{seg.endFrame}）"); continue; }

                    var dst = Slice(src.source, a, b, fps, src.bakeRootY);
                    string outPath = $"{folder}/{seg.name}.anim";
                    Save(dst, outPath);
                    produced.Add(outPath);
                    count++;
                    report.AppendLine($"✓ {src.label} / {seg.name}：第 {a}~{b} 格（{(b - a) / fps:F2} 秒）");
                }
            }
            // 切割表裡已經沒有的舊輸出（合併、改名、停用後留下的）一律刪掉，避免看到過時的分段而誤用
            int removed = 0;
            foreach (string folder in weaponFolders)
            {
                if (!AssetDatabase.IsValidFolder(folder)) continue;
                foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (System.IO.Path.GetDirectoryName(path).Replace('\\', '/') != folder) continue;
                    if (produced.Contains(path)) continue;
                    AssetDatabase.DeleteAsset(path);
                    removed++;
                    report.AppendLine($"🗑 刪除舊檔：{System.IO.Path.GetFileName(path)}");
                }
            }

            report.Insert(0, $"共 {count} 段，刪除舊檔 {removed} 個\n");
            return report.ToString();
        }

        private static AnimationClip Slice(AnimationClip src, int startFrame, int endFrame, float fps, bool bakeRootY = false)
        {
            float t0 = startFrame / fps;
            int frames = endFrame - startFrame;
            var dst = new AnimationClip { frameRate = fps };

            // 數值曲線：逐格重新取樣（來源本身就是逐格烘焙，取樣不會失真）
            foreach (var binding in AnimationUtility.GetCurveBindings(src))
            {
                var curve = AnimationUtility.GetEditorCurve(src, binding);
                if (curve == null) continue;
                var keys = new Keyframe[frames + 1];
                for (int f = 0; f <= frames; f++)
                    keys[f] = new Keyframe(f / fps, curve.Evaluate(t0 + f / fps));
                var outCurve = new AnimationCurve(keys);
                for (int i = 0; i < keys.Length; i++) AnimationUtility.SetKeyLeftTangentMode(outCurve, i, AnimationUtility.TangentMode.ClampedAuto);
                for (int i = 0; i < keys.Length; i++) AnimationUtility.SetKeyRightTangentMode(outCurve, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetEditorCurve(dst, binding, outCurve);
            }

            // 物件參照曲線（目前主角動畫沒有，保險起見一併處理）
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(src))
            {
                var refKeys = AnimationUtility.GetObjectReferenceCurve(src, binding);
                if (refKeys == null || refKeys.Length == 0) continue;
                var kept = new List<ObjectReferenceKeyframe>();
                ObjectReferenceKeyframe? before = null;
                foreach (var k in refKeys)
                {
                    if (k.time <= t0) before = k;
                    else if (k.time <= endFrame / fps) kept.Add(new ObjectReferenceKeyframe { time = k.time - t0, value = k.value });
                }
                if (before.HasValue) kept.Insert(0, new ObjectReferenceKeyframe { time = 0f, value = before.Value.value });
                AnimationUtility.SetObjectReferenceCurve(dst, binding, kept.ToArray());
            }

            // Root Transform 等設定沿用原動畫，只改成不循環、時間範圍為整段
            var s = AnimationUtility.GetAnimationClipSettings(src);
            s.loopTime = false;
            s.startTime = 0f;
            s.stopTime = frames / fps;
            s.cycleOffset = 0f;
            // 跳躍類：上下位移烘進姿勢，角色看起來會真的跳起來（遊戲只吃水平根位移，見 PlayerController.ApplyPendingRootMotion）
            if (bakeRootY)
            {
                s.loopBlendPositionY = true;
                s.keepOriginalPositionY = true;   // 以原始高度為準，不另外對齊腳底
            }
            AnimationUtility.SetAnimationClipSettings(dst, s);

            // 動畫事件：落在範圍內的搬過來（目前主角攻擊動畫都沒有事件）
            var events = AnimationUtility.GetAnimationEvents(src)
                .Where(e => e.time >= t0 && e.time <= endFrame / fps)
                .Select(e => { e.time -= t0; return e; })
                .ToArray();
            AnimationUtility.SetAnimationEvents(dst, events);
            return dst;
        }

        private static void Save(AnimationClip dst, string outPath)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(outPath);
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(dst, existing);   // 保留 GUID
                existing.name = name;
                EditorUtility.SetDirty(existing);
                // 只存這一個檔：不用 AssetDatabase.SaveAssets()，否則會把編輯器裡其他被弄髒的資產
                // （例如 TextMeshPro 動態字型的字元表）一起寫進硬碟，製造不相干的 git 變更。
                AssetDatabase.SaveAssetIfDirty(existing);
            }
            else
            {
                dst.name = name;
                AssetDatabase.CreateAsset(dst, outPath);
            }
        }

        private static string EnsureFolder(string weapon)
        {
            if (!AssetDatabase.IsValidFolder(OutputRoot)) AssetDatabase.CreateFolder(AnimRoot, "Sliced");
            string w = string.IsNullOrEmpty(weapon) ? "Misc" : weapon;
            string folder = $"{OutputRoot}/{w}";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(OutputRoot, w);
            return folder;
        }

        // ════════════════════════════════════════════════════════════
        //  預設切割表
        //  切點來源：在預覽場景逐格取樣雙手速度（相對骨盆），取「兩次揮擊之間速度最低」的那一格；
        //  只採用停頓明顯（速度 ≤ 最高速度約 25%）的切點，太短（< 約 0.35 秒）的段落併到相鄰段。
        //  這是自動分析的初稿，請動畫組在 Animation 視窗確認後微調影格。
        // ════════════════════════════════════════════════════════════

        private static ComboSliceTable CreateOrResetDefaultTable()
        {
            var table = AssetDatabase.LoadAssetAtPath<ComboSliceTable>(TablePath);
            bool isNew = table == null;
            if (isNew) table = ScriptableObject.CreateInstance<ComboSliceTable>();
            table.sources = new List<SliceSource>();

            string hum = AnimRoot + "/Update/Humanoid";
            string knife = AnimRoot + "/Update/PaintKnife";
            string pal = AnimRoot + "/Update/Palette";

            // ── 鉛筆（WeaponActions 目前指到 Generic 版，這裡改用 Update/Humanoid 已轉好的版本）──
            Add(table, "鉛筆 Combo1", "Pencil", Anim($"{hum}/combo1_pencil_Humanoid.anim"),
                "揮擊高峰 0.75 / 1.03 / 1.55 秒。0.88 秒處停頓不明顯，只在 1.37 秒切一刀。",
                (0, 33), (33, 44));
            Add(table, "鉛筆 Combo2", "Pencil", Anim($"{hum}/combo2_pencil_Humanoid.anim"),
                "四下揮擊、停頓非常乾淨（三個切點速度都只有 3~4%），最適合當普攻鏈。",
                (0, 19), (19, 30), (30, 41), (41, 55));
            Add(table, "鉛筆 Combo3", "Pencil", Anim($"{hum}/combo3_pencil_Humanoid.anim"),
                "第一段很長（0~1.87 秒只有 0.53 秒一個高峰，後面可能是蓄力或位移）。",
                (0, 45), (45, 64), (64, 75));
            Add(table, "鉛筆 Combo4", "Pencil", Anim($"{hum}/combo4_pencil_Humanoid.anim"),
                "揮擊高峰 0.20 / 0.75 / 1.15 / 1.40 / 1.67 / 2.28 秒；1.00~1.53 之間是連續快打，合成一段。",
                (0, 12), (12, 24), (24, 37), (37, 50), (50, 70));

            // ── 水彩筆 ──
            Add(table, "水彩筆 Combo1", "Brush", Anim($"{hum}/combo1_brush_Humanoid.anim"),
                "0.58 秒處停頓不明顯（47%），不切。",
                (0, 24), (24, 40), (40, 62));
            Add(table, "水彩筆 Combo2（未切）", "Brush", Anim($"{hum}/combo2_brush_Humanoid.anim"),
                "⚠ 只有 0.65 秒一個揮擊，0.95 秒後手幾乎不動、但動畫長 4.00 秒。可能是企劃寫的「長按轉圈」，或尾巴太長，請動畫組確認。暫不切。",
                (0, 96));
            Add(table, "水彩筆 Combo3", "Brush", Anim($"{hum}/combo3_brush_Humanoid.anim"),
                "九個高峰，前 1.1 秒是連續快打；切成五段。",
                (0, 17), (17, 40), (40, 71), (71, 89), (89, 104));
            Add(table, "水彩筆 Combo4", "Brush", Anim($"{hum}/combo4_brush_Humanoid.anim"),
                "1.82 / 2.13 秒處停頓不明顯，1.42~2.68 合成一段。",
                (0, 13), (13, 34), (34, 64), (64, 77));

            // ── 畫刀 ──
            Add(table, "畫刀 Combo1", "PaintKnife", FbxClip($"{knife}/Muir@combo1_paintKnife.fbx"),
                "0.58 秒處停頓不明顯（44%），只在 1.15 秒切。",
                (0, 28), (28, 50));
            Add(table, "畫刀 Combo2（未切）", "PaintKnife", FbxClip($"{knife}/Muir@combo2_paintKnife.fbx"),
                "⚠ 兩下揮擊在 0.18 / 0.83 秒，1.33 秒後手幾乎不動、動畫長 2.67 秒（尾巴約 1.3 秒）。暫不切，建議動畫組確認尾巴是否必要。",
                (0, 64));
            Add(table, "畫刀 Combo3", "PaintKnife", FbxClip($"{knife}/Muir@combo3_paintKnife.fbx"),
                "0.63 秒處停頓不明顯（29%），只在 1.47 秒切。",
                (0, 35), (35, 68));
            Add(table, "畫刀 Combo4", "PaintKnife", FbxClip($"{knife}/Muir@combo4_paintKnife.fbx"),
                "五下揮擊，切成五段（第 2 段只有 0.38 秒，是快速的一刀）。",
                (0, 17), (17, 26), (26, 36), (36, 58), (58, 80));

            // ── 調色盤 ──
            Add(table, "調色盤 Combo1", "Palette", FbxClip($"{pal}/Muir@combo1_palette.fbx"),
                "第一段長 2.05 秒（0.75 秒一個高峰後有長停頓，可能是瞄準/蓄力）。最後 0.3 秒併入第二段。",
                (0, 49), (49, 68));
            Add(table, "調色盤 Combo2", "Palette", FbxClip($"{pal}/Muir@combo2_palette.fbx"),
                "三下揮擊，停頓乾淨。",
                (0, 32), (32, 52), (52, 79));
            Add(table, "調色盤 Combo3", "Palette", FbxClip($"{pal}/Muir@combo3_palette.fbx"),
                "前 1.3 秒幾乎沒有揮擊（起手很長）。",
                (0, 50), (50, 59), (59, 77));
            Add(table, "調色盤 Combo4", "Palette", FbxClip($"{pal}/Muir@combo4_palette.fbx"),
                "三段，停頓乾淨。",
                (0, 10), (10, 36), (36, 48));

            if (isNew) AssetDatabase.CreateAsset(table, TablePath);
            else EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssetIfDirty(table);
            return table;
        }

        private static void Add(ComboSliceTable table, string label, string weapon, AnimationClip clip, string note,
                                params (int start, int end)[] ranges)
        {
            var src = new SliceSource { label = label, weapon = weapon, source = clip, note = note };
            if (clip == null) src.note = "⚠ 找不到原始動畫。" + note;
            string baseName = clip != null ? clip.name.Replace("_Humanoid", "") : label;
            bool single = ranges.Length == 1;
            src.enabled = !single;   // 只有一段（未切）的預設不產生
            for (int i = 0; i < ranges.Length; i++)
                src.segments.Add(new SliceSegment
                {
                    name = $"{baseName}_s{i + 1}",
                    startFrame = ranges[i].start,
                    endFrame = ranges[i].end,
                });
            table.sources.Add(src);
        }

        private static AnimationClip Anim(string path) => AssetDatabase.LoadAssetAtPath<AnimationClip>(path);

        private static AnimationClip FbxClip(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
    }

    // ════════════════════════════════════════════════════════════════
    //  切割表 Inspector：加一顆「產生切割動畫」按鈕
    // ════════════════════════════════════════════════════════════════
    [CustomEditor(typeof(ComboSliceTable))]
    public class ComboSliceTableEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "影格以原動畫的 Animation 視窗格數為準（24 fps）。\n" +
                "調好後按下方按鈕，輸出到 animation/Sliced/<武器>/，原動畫不會被修改。",
                MessageType.Info);
            if (GUILayout.Button("產生切割動畫", GUILayout.Height(28)))
            {
                AssetDatabase.SaveAssetIfDirty(target);   // 先存表本身（Inspector 剛改的影格）
                Debug.Log(ComboClipSlicer.SliceAll((ComboSliceTable)target));
            }
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }
    }
}
#endif
