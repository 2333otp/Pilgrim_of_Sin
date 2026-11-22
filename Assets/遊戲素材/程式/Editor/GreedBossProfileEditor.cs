#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PilgrimOfSin.StateMachine.EditorTools
{
    /// <summary>
    /// GreedBossProfile 的 Inspector：檢查設定錯誤 + 「同步到 Animator」按鈕。
    /// 同步 = 每個招式 / 空襲動畫在 Animator 裡建立（或更新）同名 state，程式用 CrossFade 直接播。
    /// </summary>
    [CustomEditor(typeof(GreedBossProfile))]
    public class GreedBossProfileEditor : Editor
    {
        // Animator 裡由 int State 參數驅動的基本 state，招式不可用這些名字（否則會蓋掉它們的動畫）
        private static readonly HashSet<string> Reserved = new HashSet<string> { "Idle", "Run", "Damage", "Death" };

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var p = (GreedBossProfile)target;
            EditorGUILayout.Space(10);
            foreach (var msg in Validate(p))
                EditorGUILayout.HelpBox(msg, MessageType.Warning);

            EditorGUILayout.Space(4);
            using (new EditorGUI.DisabledScope(p.animatorController == null))
            {
                if (GUILayout.Button("同步到 Animator（建立 / 更新招式與空襲的動畫 state）", GUILayout.Height(30)))
                    SyncToAnimator(p);
            }
            if (p.animatorController == null)
                EditorGUILayout.HelpBox("請先在最上面指定 Animator Controller（GreedBoss.controller）。", MessageType.Info);
        }

        public static List<string> Validate(GreedBossProfile p)
        {
            var list = new List<string>();
            var ids = new HashSet<string>();
            foreach (var m in p.moves)
            {
                if (m == null) continue;
                if (string.IsNullOrEmpty(m.id)) { list.Add("有招式沒有填名稱（id）。"); continue; }
                if (!ids.Add(m.id)) list.Add($"招式名稱重複：{m.id}");
                if (Reserved.Contains(m.id) || IsAerialName(m.id)) list.Add($"招式名稱「{m.id}」是保留名稱，請換一個。");
                if (m.clip == null) list.Add($"{m.id}：沒有放動畫，這招不會出。");
                foreach (var w in m.hitWindows)
                    if (w.end <= w.start) list.Add($"{m.id}：有命中區間的結束 ≤ 開始。");
                if (m.lungeDistance > 0f && m.lungeEnd <= m.lungeStart) list.Add($"{m.id}：突進的結束 ≤ 開始，不會移動。");
            }
            foreach (var m in p.moves)
            {
                if (m == null) continue;
                foreach (var b in m.branches)
                {
                    if (string.IsNullOrEmpty(b.toMove)) list.Add($"{m.id}：有派生沒填要接哪一招。");
                    else if (!ids.Contains(b.toMove)) list.Add($"{m.id}：派生目標「{b.toMove}」不在招式清單裡。");
                }
            }
            if (!p.moves.Any(m => m != null && m.IsValid && m.canOpen))
                list.Add("沒有任何「可當起手招」的招式，Boss 只會追著主角跑。");

            var a = p.aerialKick;
            if (a.flyClip == null || a.kickClip == null || a.diveClip == null || a.fallClip == null)
                list.Add("空襲的 Fly / Kick / Dive / Fall 動畫沒放齊：平衡結束時會退回舊流程（Boss 站著、天秤自己翻倒）。");
            if (a.liftOffRatio > a.flyHoldRatio) list.Add("空襲：離地比例 > 起飛停格幀，會在停格幀就離地。");
            if (a.diveLaunchRatio > a.diveHoldRatio) list.Add("空襲：俯衝出發比例 > 俯衝停格幀，會在停格幀就出發。");
            return list;
        }

        private static bool IsAerialName(string s)
            => s == GreedBossController.AnimStateFly || s == GreedBossController.AnimStateKick
            || s == GreedBossController.AnimStateDive || s == GreedBossController.AnimStateFall;

        public static void SyncToAnimator(GreedBossProfile p)
        {
            var ac = p.animatorController as AnimatorController;
            if (ac == null) { Debug.LogError("[GreedBossProfile] Animator Controller 不是 AnimatorController 資產。"); return; }

            var sm = ac.layers[0].stateMachine;
            Undo.RecordObject(sm, "Sync Greed Boss Animator");

            var wanted = new List<(string name, AnimationClip clip)>();
            foreach (var m in p.moves)
                if (m != null && m.IsValid && !Reserved.Contains(m.id) && !IsAerialName(m.id)) wanted.Add((m.id, m.clip));
            var a = p.aerialKick;
            if (a.flyClip) wanted.Add((GreedBossController.AnimStateFly, a.flyClip));
            if (a.kickClip) wanted.Add((GreedBossController.AnimStateKick, a.kickClip));
            if (a.diveClip) wanted.Add((GreedBossController.AnimStateDive, a.diveClip));
            if (a.fallClip) wanted.Add((GreedBossController.AnimStateFall, a.fallClip));

            int created = 0, updated = 0, col = 0;
            foreach (var (name, clip) in wanted)
            {
                var existing = sm.states.FirstOrDefault(s => s.state.name == name).state;
                if (existing == null)
                {
                    var pos = new Vector3(650f + (col % 2) * 220f, 0f + (col / 2) * 60f, 0f);
                    existing = sm.AddState(name, pos);
                    existing.writeDefaultValues = true;
                    created++;
                    col++;
                }
                if (existing.motion != clip) { Undo.RecordObject(existing, "Sync Greed Boss Animator"); existing.motion = clip; updated++; }
                existing.speed = 1f;   // 播放速度由程式 Animator.speed 控制
            }

            EditorUtility.SetDirty(ac);
            AssetDatabase.SaveAssets();
            Debug.Log($"[GreedBossProfile] 同步完成：新增 {created} 個 state、更新 {updated} 個動畫。");
        }

        // ════════════════════════════════════════════════════════════
        //  建立預設資產
        // ════════════════════════════════════════════════════════════

        private const string AssetPath = "Assets/遊戲素材/SO/GreedBossProfile.asset";
        private const string AnimFolder = "Assets/遊戲素材/敵人/貪/Animation";
        private const string ControllerPath = "Assets/遊戲素材/敵人/貪/GreedBoss.controller";

        [MenuItem("PilgrimOfSin/貪/建立預設 Boss 招式表")]
        public static void CreateDefault()
        {
            if (AssetDatabase.LoadAssetAtPath<GreedBossProfile>(AssetPath) != null)
            {
                Debug.LogWarning($"[GreedBossProfile] {AssetPath} 已存在，不覆蓋。");
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<GreedBossProfile>(AssetPath);
                return;
            }

            var p = CreateInstance<GreedBossProfile>();
            p.animatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);

            // 數值沿用改版前場景上的設定（Attack1 範圍 3m / 傷害 900 / 判定球 1.3, 1.0, r1.5）；
            // 命中區間依動畫裡翅膀揮最快的時間點抓的，可再微調。
            var a1 = new GreedMoveEntry
            {
                id = "Attack1", clip = Clip("Attack1"), canOpen = true, openerWeight = 2f,
                openerCondition = new GreedMoveCondition { maxDistance = 3f },
                trackUntilRatio = 0.5f, damage = 900f, recoveryIdle = 1f,
                hitWindows = { new GreedHitWindow { start = 0.70f, end = 0.82f, forward = 1.3f, height = 1f, radius = 1.5f } },
                branches =
                {
                    // 範例：主角還貼在身邊時，40% 機率追加 Attack3
                    new GreedMoveBranch { toMove = "Attack3", atRatio = 0.85f, chance = 0.4f,
                                          condition = new GreedMoveCondition { maxDistance = 3.5f } },
                },
            };
            var a2 = new GreedMoveEntry
            {
                id = "Attack2", clip = Clip("Attack2"), canOpen = true, openerWeight = 1f, cooldown = 3f,
                openerCondition = new GreedMoveCondition { minDistance = 3f, maxDistance = 6f },
                trackUntilRatio = 0.15f, lungeDistance = 2.5f, lungeStart = 0.08f, lungeEnd = 0.2f,
                damage = 1200f, recoveryIdle = 1.2f,
                hitWindows =
                {
                    new GreedHitWindow { start = 0.18f, end = 0.26f, forward = 1.8f, height = 1f, radius = 1.6f },
                    new GreedHitWindow { start = 0.44f, end = 0.52f, forward = 1.3f, height = 1f, radius = 1.5f },
                },
                branches =
                {
                    // 範例：錢袋重（Boss 強化）時，50% 機率接 Attack1
                    new GreedMoveBranch { toMove = "Attack1", atRatio = 0.9f, chance = 0.5f,
                                          condition = new GreedMoveCondition { maxDistance = 3.5f, phases = GreedPhaseMask.錢袋重 } },
                },
            };
            var a3 = new GreedMoveEntry
            {
                id = "Attack3", clip = Clip("Attack3"), canOpen = true, openerWeight = 1f, cooldown = 4f,
                openerCondition = new GreedMoveCondition { maxDistance = 3f },
                trackUntilRatio = 0.6f, damage = 2300f, recoveryIdle = 1.5f,
                hitWindows =
                {
                    new GreedHitWindow { start = 0.12f, end = 0.35f, forward = 1.3f, height = 1f, radius = 1.5f },
                    new GreedHitWindow { start = 0.65f, end = 0.73f, forward = 1.3f, height = 1f, radius = 1.5f },
                    new GreedHitWindow { start = 0.88f, end = 0.95f, forward = 1.3f, height = 1f, radius = 1.5f },
                },
            };
            p.moves.Add(a1); p.moves.Add(a2); p.moves.Add(a3);

            p.aerialKick.flyClip = Clip("Fly");
            p.aerialKick.kickClip = Clip("Kick");
            p.aerialKick.diveClip = Clip("Dive");
            p.aerialKick.fallClip = Clip("Fall");

            AssetDatabase.CreateAsset(p, AssetPath);
            AssetDatabase.SaveAssets();
            SyncToAnimator(p);
            Selection.activeObject = p;
            Debug.Log($"[GreedBossProfile] 已建立 {AssetPath}");
        }

        private static AnimationClip Clip(string name)
            => AssetDatabase.LoadAllAssetsAtPath($"{AnimFolder}/Chicken@{name}.fbx")
                            .OfType<AnimationClip>()
                            .FirstOrDefault(c => !c.name.StartsWith("__preview"));
    }
}
#endif
