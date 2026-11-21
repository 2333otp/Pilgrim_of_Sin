#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PilgrimOfSin.StateMachine.EditorTools
{
    /// <summary>
    /// 自動抓出招表每一招的命中區間（每一下揮擊各一個）。
    ///
    /// 做法：在預覽場景放一隻主角 Player.prefab，逐格（24 fps）播動畫，量左右手相對骨盆的移動速度，
    ///       取兩手較快的那一隻、稍微平滑後，速度超過「這段動畫最高速 × 門檻比例」的連續影格就是一下揮擊，
    ///       前後各多留 1 格當命中區間。太近的兩段（中間只差 1~2 格）合成一下。
    /// 注意：主角骨架縮放約 100 倍，一定要用世界座標量距離（InverseTransformPoint 會被縮放吃掉，量起來全是 0）。
    /// 這只是初版預設值，抓完請在 Inspector 看過、依實際命中手感調整。
    /// </summary>
    public static class ComboHitWindowAnalyzer
    {
        private const string PlayerPrefabPath = "Assets/遊戲素材/預製物/Player.prefab";
        private const string WeaponActionsFolder = "Assets/遊戲素材/SO/主角";
        private const string HumanoidFolder = "Assets/遊戲素材/角色/謬爾/animation/Update/Humanoid";
        private static readonly string[] Weapons = { "Pencil", "Brush", "PaintKnife", "Palette" };

        private const float SampleFps = 24f;
        private const float PeakRatio = 0.45f;      // 超過這段動畫最高速的 45% 才算在揮
        private const float MinSwingSpeed = 1.5f;   // 最高速低於這個（公尺/秒）時，用這個當門檻，避免把小動作當揮擊
        private const int PadFrames = 1;            // 命中區間前後各多留幾格
        private const int MergeGapFrames = 2;       // 兩段之間只差這麼多格就合成一下

        // ════════════════════════════════════════════════════════════
        //  選單
        // ════════════════════════════════════════════════════════════

        [MenuItem("PilgrimOfSin/主角/出招表/自動抓命中區間（只填還沒設的招）")]
        private static void FillEmptyMenu() => Debug.Log(RunAll(overwrite: false));

        [MenuItem("PilgrimOfSin/主角/出招表/自動抓命中區間（全部重抓，會蓋掉手調的）")]
        private static void OverwriteAllMenu()
        {
            if (!EditorUtility.DisplayDialog("自動抓命中區間", "四把武器所有招式的命中區間都會重抓，手動調過的會被蓋掉。確定嗎？", "全部重抓", "取消"))
                return;
            Debug.Log(RunAll(overwrite: true));
        }

        /// <summary>四把武器全部處理，回傳報告文字。</summary>
        public static string RunAll(bool overwrite)
        {
            var report = new StringBuilder($"[ComboHitWindowAnalyzer] 自動抓命中區間（{(overwrite ? "全部重抓" : "只填還沒設的")}）\n");
            using (var sampler = new Sampler())
            {
                if (!sampler.Valid) return "[ComboHitWindowAnalyzer] 找不到主角 Player.prefab 或它不是 Humanoid，無法分析。";
                foreach (string w in Weapons)
                {
                    var set = AssetDatabase.LoadAssetAtPath<WeaponActionSet>($"{WeaponActionsFolder}/WeaponActions_{w}.asset");
                    if (set == null) { report.AppendLine($"✗ 找不到 WeaponActions_{w}"); continue; }
                    report.AppendLine($"── {w} ──");
                    Fill(set, overwrite, sampler, report);
                }
            }
            AssetDatabase.SaveAssets();
            return report.ToString();
        }

        /// <summary>處理一把武器（Inspector 按鈕也用這個）。</summary>
        public static string RunOne(WeaponActionSet set, bool overwrite)
        {
            var report = new StringBuilder($"[ComboHitWindowAnalyzer] {set.name}\n");
            using (var sampler = new Sampler())
            {
                if (!sampler.Valid) return "[ComboHitWindowAnalyzer] 找不到主角 Player.prefab 或它不是 Humanoid，無法分析。";
                Fill(set, overwrite, sampler, report);
            }
            AssetDatabase.SaveAssets();
            return report.ToString();
        }

        private static void Fill(WeaponActionSet set, bool overwrite, Sampler sampler, StringBuilder report)
        {
            var c = set.combo;
            var slots = new (string name, ComboMoveEntry move)[]
            {
                ("N1", c.n1), ("N2", c.n2), ("N3", c.n3), ("N4", c.n4),
                ("C1", c.c1), ("C2", c.c2), ("C3", c.c3), ("C4", c.c4),
                ("停頓連打", c.pauseCombo), ("蓄力", c.chargeHold),
            };

            Undo.RecordObject(set, "自動抓命中區間");
            foreach (var (name, move) in slots)
            {
                if (move == null || !move.IsValid) continue;
                if (move.HasHitWindows && !overwrite) { report.AppendLine($"  {name}：已經有 {move.hitWindows.Count} 下，略過"); continue; }

                var windows = Analyze(sampler, move.clip);
                move.hitWindows = windows;
                var sb = new StringBuilder();
                foreach (var hw in windows)
                    sb.Append($"{hw.start * move.clip.length:F2}~{hw.end * move.clip.length:F2}s  ");
                report.AppendLine($"  {name} {move.clip.name}：{windows.Count} 下　{sb}");
            }

            // 秘劍：動畫在動作清單的 SpecialSkill 那一筆
            AnimationClip special = null;
            foreach (var e in set.actions)
                if (e.action == PlayerActionId.SpecialSkill && e.clip != null) { special = e.clip; break; }
            if (special == null) report.AppendLine("  秘劍：動作清單沒有放動畫，略過");
            else if (c.specialHitWindows != null && c.specialHitWindows.Count > 0 && !overwrite)
                report.AppendLine($"  秘劍：已經有 {c.specialHitWindows.Count} 下，略過");
            else
            {
                // Generic 動畫放在 Humanoid 主角上量不到手的位置：改用 Update/Humanoid 裡轉好的同名動畫（時間軸相同）
                var sampleClip = special;
                string note = "";
                if (!special.humanMotion)
                {
                    var humanoid = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{HumanoidFolder}/{special.name}_Humanoid.anim");
                    if (humanoid != null) { sampleClip = humanoid; note = $"（用 {humanoid.name} 量）"; }
                    else note = "（⚠ Generic 動畫、找不到 Humanoid 版，結果可能不準）";
                }
                var windows = Analyze(sampler, sampleClip);
                // 區間是比例：用來量的動畫長度跟實際播的不同時，比例照樣適用
                c.specialHitWindows = windows;
                var sb = new StringBuilder();
                foreach (var hw in windows)
                    sb.Append($"{hw.start * special.length:F2}~{hw.end * special.length:F2}s  ");
                report.AppendLine($"  秘劍 {special.name}{note}：{windows.Count} 下　{sb}");
            }
            EditorUtility.SetDirty(set);
        }

        // ════════════════════════════════════════════════════════════
        //  分析
        // ════════════════════════════════════════════════════════════

        /// <summary>回傳這段動畫的命中區間（動畫長度比例）。</summary>
        public static List<HitWindow> Analyze(AnimationClip clip)
        {
            using (var sampler = new Sampler())
                return sampler.Valid ? Analyze(sampler, clip) : new List<HitWindow>();
        }

        private static List<HitWindow> Analyze(Sampler sampler, AnimationClip clip)
        {
            int frames = Mathf.Max(1, Mathf.RoundToInt(clip.length * SampleFps));
            float[] speed = sampler.HandSpeeds(clip, frames);   // speed[f] = 第 f-1 → f 格的速度，speed[0] = 0

            // 平滑（1-2-1），避免單格雜訊切出很多碎段
            var smooth = new float[speed.Length];
            for (int f = 0; f < speed.Length; f++)
            {
                float a = speed[Mathf.Max(0, f - 1)], b = speed[f], d = speed[Mathf.Min(speed.Length - 1, f + 1)];
                smooth[f] = (a + 2f * b + d) * 0.25f;
            }

            float peak = 0f; int peakFrame = 0;
            for (int f = 1; f < smooth.Length; f++)
                if (smooth[f] > peak) { peak = smooth[f]; peakFrame = f; }
            float threshold = Mathf.Max(peak * PeakRatio, Mathf.Min(MinSwingSpeed, peak * 0.9f));

            // 超過門檻的連續影格 = 一下揮擊（以影格範圍記錄）
            var segs = new List<Vector2Int>();
            int startF = -1;
            for (int f = 1; f < smooth.Length; f++)
            {
                bool on = smooth[f] >= threshold;
                if (on && startF < 0) startF = f - 1;          // 第 f-1 → f 格在動，判定從 f-1 開始
                if (!on && startF >= 0) { segs.Add(new Vector2Int(startF, f - 1)); startF = -1; }
            }
            if (startF >= 0) segs.Add(new Vector2Int(startF, frames));

            // 太近的合成一下
            for (int i = segs.Count - 1; i > 0; i--)
            {
                if (segs[i].x - segs[i - 1].y <= MergeGapFrames)
                {
                    segs[i - 1] = new Vector2Int(segs[i - 1].x, segs[i].y);
                    segs.RemoveAt(i);
                }
            }
            if (segs.Count == 0) segs.Add(new Vector2Int(Mathf.Max(0, peakFrame - 2), Mathf.Min(frames, peakFrame + 1)));

            var result = new List<HitWindow>();
            foreach (var s in segs)
            {
                int a = Mathf.Max(0, s.x - PadFrames);
                int b = Mathf.Min(frames, s.y + PadFrames);
                if (result.Count > 0)
                {
                    // 跟上一下重疊時從上一下結束後開始，保持每一下各自獨立
                    float prevEnd = result[result.Count - 1].end;
                    a = Mathf.Max(a, Mathf.CeilToInt(prevEnd * frames));
                }
                if (b <= a) continue;
                result.Add(new HitWindow { start = Round(a / (float)frames), end = Round(b / (float)frames) });
            }
            return result;
        }

        private static float Round(float v) => Mathf.Round(v * 1000f) / 1000f;

        /// <summary>在預覽場景放一隻主角，量動畫中雙手的速度。用完要 Dispose。</summary>
        private sealed class Sampler : System.IDisposable
        {
            private readonly UnityEngine.SceneManagement.Scene _scene;
            private readonly GameObject _go;
            private readonly Transform _hips, _rightHand, _leftHand;
            public bool Valid => _go != null && _hips != null && _rightHand != null && _leftHand != null;

            public Sampler()
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
                if (prefab == null) return;
                _scene = EditorSceneManager.NewPreviewScene();
                _go = Object.Instantiate(prefab);
                _go.transform.localScale = Vector3.one;   // Prefab 是 (1, 1.7, 1)，場景裡都覆寫回 1，跟遊戲一致
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_go, _scene);
                var anim = _go.GetComponent<Animator>();
                if (anim == null || !anim.isHuman) return;
                _hips = anim.GetBoneTransform(HumanBodyBones.Hips);
                _rightHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
                _leftHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            }

            public float[] HandSpeeds(AnimationClip clip, int frames)
            {
                var speeds = new float[frames + 1];
                Vector3 prevR = Vector3.zero, prevL = Vector3.zero;
                for (int f = 0; f <= frames; f++)
                {
                    clip.SampleAnimation(_go, Mathf.Min(clip.length, f / SampleFps));
                    // 世界座標（骨架縮放 ~100 倍，不能用 InverseTransformPoint）
                    var inv = Quaternion.Inverse(_hips.rotation);
                    Vector3 r = inv * (_rightHand.position - _hips.position);
                    Vector3 l = inv * (_leftHand.position - _hips.position);
                    if (f > 0) speeds[f] = Mathf.Max((r - prevR).magnitude, (l - prevL).magnitude) * SampleFps;
                    prevR = r; prevL = l;
                }
                return speeds;
            }

            public void Dispose()
            {
                if (_go != null) Object.DestroyImmediate(_go);
                if (_scene.IsValid()) EditorSceneManager.ClosePreviewScene(_scene);
            }
        }
    }
}
#endif
