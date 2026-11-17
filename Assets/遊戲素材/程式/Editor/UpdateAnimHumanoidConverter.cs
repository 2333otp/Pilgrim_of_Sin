#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace PilgrimOfSin.StateMachine.EditorTools
{
    /// <summary>
    /// 把 animation/Update 資料夾裡的更新版動畫（Generic 骨頭曲線 .anim）轉成 Humanoid 動畫。
    ///
    /// 原因：Player 的 Animator 是 Humanoid，直接播 Generic 骨頭曲線的 .anim 會讓角色遁地。
    /// 做法：把 .anim 逐格套到玩家模型上，用 HumanPoseHandler 讀出人形姿勢（RootT / RootQ / 肌肉），
    ///       寫成新的 Humanoid .anim；頭髮、袖子等非人形骨頭的曲線原樣保留。
    ///       Root Transform 設定與鉛筆 fbx 相同（Bake Into Pose 全勾、Y = Original），不會遁地。
    /// 輸出：animation/Update/Humanoid/&lt;原名&gt;_Humanoid.anim（重複執行會覆蓋，GUID 不變）。
    /// 轉完後自動把 WeaponActions 對應項目（鉛筆、畫筆的切換武器、輕/重攻擊、連段 1~4、特殊技）的 Clip
    /// 指到轉好的版本，並把 baseClip 校正成 Animator 裡該 state 實際使用的 clip（不然替換不會生效）。
    /// 攻擊類動畫保留水平根位移（XZ 不烘進姿勢），讓角色照原設計隨招式移動。
    /// 選單：PilgrimOfSin/主角/Update 動畫轉 Humanoid
    /// </summary>
    public static class UpdateAnimHumanoidConverter
    {
        private const string SourceFolder = "Assets/遊戲素材/角色/謬爾/animation/Update";
        private const string OutputFolder = "Assets/遊戲素材/角色/謬爾/animation/Update/Humanoid";
        private const string ModelPath = "Assets/遊戲素材/角色/謬爾/Muir_new_mat_deform.fbx";
        private const string ActionsFolder = "Assets/遊戲素材/SO/主角";

        // 轉完後要改指向 Humanoid 版本的 WeaponActions 項目：(資產檔名, stateName, 來源 .anim 檔名)
        private static readonly (string asset, string state, string anim)[] AutoAssign =
        {
            ("WeaponActions_Pencil", "WeaponSwitch_Pencil", "changeWeapon_pencil"),
            ("WeaponActions_Pencil", "LightAttack_Pencil",  "SoftAttack_pencil"),
            ("WeaponActions_Pencil", "HeavyAttack_Pencil",  "HardAttack_Pencil"),
            ("WeaponActions_Pencil", "Combo1_Pencil",       "combo1_pencil"),
            ("WeaponActions_Pencil", "Combo2_Pencil",       "combo2_pencil"),
            ("WeaponActions_Pencil", "Combo3_Pencil",       "combo3_pencil"),
            ("WeaponActions_Pencil", "Combo4_Pencil",       "combo4_pencil"),
            ("WeaponActions_Pencil", "SpecialSkill_Pencil", "SP_skill_pencil"),

            ("WeaponActions_Brush",  "WeaponSwitch_Brush",  "changeWeapon_brush"),
            ("WeaponActions_Brush",  "LightAttack_Brush",   "SoftAttack_brush"),
            ("WeaponActions_Brush",  "HeavyAttack_Brush",   "HardAttack_brush"),
            ("WeaponActions_Brush",  "Combo1_Brush",        "combo1_brush"),
            ("WeaponActions_Brush",  "Combo2_Brush",        "combo2_brush"),
            ("WeaponActions_Brush",  "Combo3_Brush",        "combo3_brush"),
            ("WeaponActions_Brush",  "Combo4_Brush",        "combo4_brush"),
            ("WeaponActions_Brush",  "SpecialSkill_Brush",  "SP_skill_brush"),
        };

        // 攻擊類動畫的水平位移要保留成根位移（PlayerController.OnAnimatorMove 會吃），不能烘進姿勢；
        // 切換武器等其他動畫則全部烘進姿勢，由程式控制位移。
        private static bool KeepHorizontalRootMotion(string animName)
        {
            string n = animName.ToLowerInvariant();
            return n.StartsWith("softattack") || n.StartsWith("hardattack") || n.StartsWith("combo") || n.StartsWith("sp_skill");
        }

        [MenuItem("PilgrimOfSin/主角/Update 動畫轉 Humanoid")]
        public static void ConvertAll()
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            if (modelAsset == null || avatar == null || !avatar.isHuman)
            {
                Debug.LogError($"[UpdateAnimHumanoidConverter] 找不到玩家模型或 Humanoid Avatar：{ModelPath}");
                return;
            }

            if (!AssetDatabase.IsValidFolder(OutputFolder))
                AssetDatabase.CreateFolder(SourceFolder, "Humanoid");

            var report = new StringBuilder();
            var converted = new Dictionary<string, AnimationClip>();

            var instance = (GameObject)Object.Instantiate(modelAsset);
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                // 移除模型自帶的 Animator，避免 SampleAnimation 走 Humanoid 管線
                foreach (var a in instance.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);

                var root = instance.transform;
                var humanBoneNames = new HashSet<string>(avatar.humanDescription.human.Select(h => h.boneName));
                var handler = new HumanPoseHandler(avatar, root);

                // 記錄初始姿勢，每支動畫轉完都還原，避免上一支殘留的骨頭位置影響下一支
                var allTransforms = instance.GetComponentsInChildren<Transform>(true);
                var rest = allTransforms.ToDictionary(t => t, t => (t.localPosition, t.localRotation, t.localScale));

                foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { SourceFolder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetDirectoryName(path).Replace('\\', '/') != SourceFolder) continue; // 只轉 Update 這一層
                    var src = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if (src == null || src.isHumanMotion) continue;

                    foreach (var kv in rest) { kv.Key.localPosition = kv.Value.localPosition; kv.Key.localRotation = kv.Value.localRotation; kv.Key.localScale = kv.Value.localScale; }

                    var dst = Convert(src, instance, handler, humanBoneNames);
                    string outPath = $"{OutputFolder}/{src.name}_Humanoid.anim";
                    var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
                    if (existing != null)
                    {
                        EditorUtility.CopySerialized(dst, existing); // 保留 GUID，已引用的地方不會斷
                        existing.name = Path.GetFileNameWithoutExtension(outPath);
                        EditorUtility.SetDirty(existing);
                        converted[src.name] = existing;
                    }
                    else
                    {
                        AssetDatabase.CreateAsset(dst, outPath);
                        converted[src.name] = dst;
                    }
                    report.AppendLine($"✓ {src.name} → {Path.GetFileName(outPath)}（{src.length:F2} 秒）");
                }
                handler.Dispose();
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            AssetDatabase.SaveAssets();
            AssignWeaponSwitch(converted, report);
            AssetDatabase.SaveAssets();
            Debug.Log($"[UpdateAnimHumanoidConverter] 轉換完成 {converted.Count} 支：\n{report}");
        }

        private static AnimationClip Convert(AnimationClip src, GameObject go, HumanPoseHandler handler, HashSet<string> humanBones)
        {
            float fps = src.frameRate > 0 ? src.frameRate : 30f;
            int frames = Mathf.Max(1, Mathf.RoundToInt(src.length * fps));

            var rootT = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            var rootQ = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            var muscles = new AnimationCurve[HumanTrait.MuscleCount];
            for (int i = 0; i < muscles.Length; i++) muscles[i] = new AnimationCurve();

            var pose = new HumanPose();
            for (int f = 0; f <= frames; f++)
            {
                float t = Mathf.Min(f / fps, src.length);
                src.SampleAnimation(go, t);
                handler.GetHumanPose(ref pose);

                rootT[0].AddKey(t, pose.bodyPosition.x);
                rootT[1].AddKey(t, pose.bodyPosition.y);
                rootT[2].AddKey(t, pose.bodyPosition.z);
                rootQ[0].AddKey(t, pose.bodyRotation.x);
                rootQ[1].AddKey(t, pose.bodyRotation.y);
                rootQ[2].AddKey(t, pose.bodyRotation.z);
                rootQ[3].AddKey(t, pose.bodyRotation.w);
                for (int i = 0; i < muscles.Length; i++) muscles[i].AddKey(t, pose.muscles[i]);
            }

            var dst = new AnimationClip { name = src.name + "_Humanoid", frameRate = fps };
            void Set(string attr, AnimationCurve c) =>
                AnimationUtility.SetEditorCurve(dst, EditorCurveBinding.FloatCurve("", typeof(Animator), attr), c);

            Set("RootT.x", rootT[0]); Set("RootT.y", rootT[1]); Set("RootT.z", rootT[2]);
            Set("RootQ.x", rootQ[0]); Set("RootQ.y", rootQ[1]); Set("RootQ.z", rootQ[2]); Set("RootQ.w", rootQ[3]);
            for (int i = 0; i < muscles.Length; i++) Set(MuscleAttribute(HumanTrait.MuscleName[i]), muscles[i]);

            // 非人形骨頭（頭髮、袖子、包包等）原樣保留骨頭曲線
            foreach (var b in AnimationUtility.GetCurveBindings(src))
            {
                if (b.type != typeof(Transform) || string.IsNullOrEmpty(b.path)) continue;
                string bone = b.path.Substring(b.path.LastIndexOf('/') + 1);
                if (humanBones.Contains(bone)) continue;
                AnimationUtility.SetEditorCurve(dst, b, AnimationUtility.GetEditorCurve(src, b));
            }

            // 與鉛筆 fbx 相同的 Root Transform 設定：Y 以原始高度為準並烘進姿勢（防遁地）
            var s = AnimationUtility.GetAnimationClipSettings(src);
            s.loopTime = false;
            s.loopBlendOrientation = true;   // Root Transform Rotation：Bake Into Pose
            s.loopBlendPositionY = true;     // Root Transform Position (Y)：Bake Into Pose
            s.loopBlendPositionXZ = !KeepHorizontalRootMotion(src.name); // 攻擊類不勾，保留往前移動的根位移
            s.keepOriginalOrientation = false;
            s.keepOriginalPositionY = true;  // Based Upon = Original
            s.keepOriginalPositionXZ = false;
            s.heightFromFeet = false;
            AnimationUtility.SetAnimationClipSettings(dst, s);
            AnimationUtility.SetAnimationEvents(dst, AnimationUtility.GetAnimationEvents(src));
            return dst;
        }

        // HumanTrait.MuscleName 的手指名稱跟動畫曲線屬性名稱不同（"Left Thumb 1 Stretched" → "LeftHand.Thumb.1 Stretched"）
        private static string MuscleAttribute(string muscle)
        {
            var m = Regex.Match(muscle, @"^(Left|Right) (Thumb|Index|Middle|Ring|Little) (\d|Spread)( Stretched)?$");
            return m.Success ? $"{m.Groups[1].Value}Hand.{m.Groups[2].Value}.{m.Groups[3].Value}{m.Groups[4].Value}" : muscle;
        }

        private const string ControllerPath = "Assets/遊戲素材/角色/謬爾/Muir_Gameplay.controller";

        // Animator 裡每個 state 實際使用的 clip（含子 StateMachine）
        private static Dictionary<string, AnimationClip> StateClips()
        {
            var result = new Dictionary<string, AnimationClip>();
            var ctrl = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ControllerPath);
            if (ctrl == null) return result;
            void Walk(UnityEditor.Animations.AnimatorStateMachine sm)
            {
                foreach (var cs in sm.states)
                    if (cs.state.motion is AnimationClip c) result.TryAdd(cs.state.name, c);
                foreach (var child in sm.stateMachines) Walk(child.stateMachine);
            }
            foreach (var layer in ctrl.layers) Walk(layer.stateMachine);
            return result;
        }

        private static void AssignWeaponSwitch(Dictionary<string, AnimationClip> converted, StringBuilder report)
        {
            var stateClips = StateClips();
            foreach (var (assetName, state, anim) in AutoAssign)
            {
                if (!converted.TryGetValue(anim, out var clip)) { report.AppendLine($"✗ 找不到 {anim}，{state} 未更新"); continue; }
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>($"{ActionsFolder}/{assetName}.asset");
                if (asset == null) { report.AppendLine($"✗ 找不到 {assetName}.asset"); continue; }

                var so = new SerializedObject(asset);
                var actions = so.FindProperty("actions");
                bool found = false;
                for (int i = 0; actions != null && i < actions.arraySize; i++)
                {
                    var e = actions.GetArrayElementAtIndex(i);
                    if (e.FindPropertyRelative("stateName").stringValue != state) continue;
                    e.FindPropertyRelative("clip").objectReferenceValue = clip;
                    // baseClip 必須是 Animator 該 state 實際用的 clip，執行時的 Override 才換得到
                    if (stateClips.TryGetValue(state, out var baseClip))
                        e.FindPropertyRelative("baseClip").objectReferenceValue = baseClip;
                    else
                        report.AppendLine($"✗ Animator 裡找不到 state「{state}」，baseClip 未校正，替換可能不會生效");
                    found = true;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                report.AppendLine(found ? $"→ {assetName}「{state}」Clip 改用 {clip.name}" : $"✗ {assetName} 沒有 {state} 項目");
            }
        }
    }
}
#endif
