#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PilgrimOfSin.StateMachine.EditorTools
{
    /// <summary>
    /// GreedBossProfile 的 Inspector：版面同所有 Boss（BossProfileEditor），另外加上招式檢查 + 「同步到 Animator」按鈕。
    /// 同步 = 每個招式 / 空襲動畫在 Animator 裡建立（或更新）同名 state，再把原始 Clip 讀回總表。
    /// </summary>
    [CustomEditor(typeof(GreedBossProfile))]
    public class GreedBossProfileEditor : BossProfileEditor
    {
        protected override void DrawTools(BossProfileBase baseProfile)
        {
            var p = (GreedBossProfile)baseProfile;
            foreach (var msg in Validate(p))
                EditorGUILayout.HelpBox(msg, MessageType.Warning);

            EditorGUILayout.Space(4);
            using (new EditorGUI.DisabledScope(p.animatorController == null))
            {
                if (GUILayout.Button("同步到 Animator（建立 / 更新招式與空襲的動畫 state，並讀取所有動作的原始動畫）", GUILayout.Height(30)))
                    SyncToAnimator(p);
            }
            if (p.animatorController == null)
                EditorGUILayout.HelpBox("請先在「動畫」指定 Animator Controller（GreedBoss.controller）。", MessageType.Info);
        }

        /// <summary>基本動作（待機 / 移動 / 受擊 / 死亡）用的 state，招式不能取這些名字（否則會蓋掉它們的動畫）。</summary>
        private static HashSet<string> Reserved(GreedBossProfile p)
            => new HashSet<string>(new[] { p.idle, p.move, p.hurt, p.dead }.Where(e => e != null).Select(e => e.stateName));

        private static IEnumerable<BossActionEntry> Aerial(GreedBossProfile p)
            => new[] { p.aerialKick.fly, p.aerialKick.kick, p.aerialKick.dive, p.aerialKick.fall };

        public static List<string> Validate(GreedBossProfile p)
        {
            var list = new List<string>();
            var reserved = Reserved(p);
            var aerialNames = new HashSet<string>(Aerial(p).Select(e => e.stateName));
            var ids = new HashSet<string>();
            foreach (var m in p.moves)
            {
                if (m == null) continue;
                if (string.IsNullOrEmpty(m.stateName)) { list.Add("有招式沒有填名稱。"); continue; }
                if (!ids.Add(m.stateName)) list.Add($"招式名稱重複：{m.stateName}");
                if (reserved.Contains(m.stateName) || aerialNames.Contains(m.stateName)) list.Add($"招式名稱「{m.stateName}」和基本動作 / 空襲的 state 重名，請換一個。");
                if (m.EffectiveClip == null) list.Add($"{m.stateName}：沒有放動畫，這招不會出。");
                foreach (var w in m.hitWindows)
                    if (w.end <= w.start) list.Add($"{m.stateName}：有命中區間的結束 ≤ 開始。");
                if (m.hitWindows.Count == 0 && m.hitRadius <= 0f) list.Add($"{m.stateName}：沒有命中區間、判定距離也是 0，這招打不到人。");
                if (m.lungeDistance > 0f && m.lungeEnd <= m.lungeStart) list.Add($"{m.stateName}：突進的結束 ≤ 開始，不會移動。");
            }
            foreach (var m in p.moves)
            {
                if (m == null) continue;
                foreach (var b in m.branches)
                {
                    if (string.IsNullOrEmpty(b.toMove)) list.Add($"{m.stateName}：有派生沒填要接哪一招。");
                    else if (!ids.Contains(b.toMove)) list.Add($"{m.stateName}：派生目標「{b.toMove}」不在招式清單裡。");
                }
            }
            if (!p.moves.Any(m => m != null && m.IsValid && m.canOpen))
                list.Add("沒有任何「可當起手招」的招式，Boss 只會追著主角跑。");

            var a = p.aerialKick;
            if (!a.IsComplete)
                list.Add("空襲的 Fly / Kick / Dive / Fall 動畫沒放齊：平衡結束時會退回舊流程（Boss 站著、天秤自己翻倒）。");
            if (a.liftOffRatio > a.flyHoldRatio) list.Add("空襲：離地比例 > 起飛停格幀，會在停格幀就離地。");
            if (a.diveLaunchRatio > a.diveHoldRatio) list.Add("空襲：俯衝出發比例 > 俯衝停格幀，會在停格幀就出發。");

            list.AddRange(ValidateStates(p).Where(s => !s.Contains("還沒有原始 Clip")));   // 同步按鈕會自動補原始 Clip
            return list;
        }

        public static void SyncToAnimator(GreedBossProfile p)
        {
            var ac = p.animatorController as AnimatorController;
            if (ac == null) { Debug.LogError("[GreedBossProfile] Animator Controller 不是 AnimatorController 資產。"); return; }

            var sm = ac.layers[0].stateMachine;
            Undo.RecordObject(sm, "Sync Greed Boss Animator");

            var reserved = Reserved(p);
            var wanted = new List<(string name, AnimationClip clip)>();
            foreach (var m in p.moves)
                if (m != null && m.IsValid && !reserved.Contains(m.stateName)) wanted.Add((m.stateName, m.EffectiveClip));
            foreach (var e in Aerial(p))
                if (e != null && e.EffectiveClip != null && !string.IsNullOrEmpty(e.stateName)) wanted.Add((e.stateName, e.EffectiveClip));

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

            // 招式 / 空襲的 Animator state 現在綁的就是總表裡的動畫 → 原始 Clip = 它；基本動作則從 Animator 讀
            SyncBaseClips(p);
        }

        // ════════════════════════════════════════════════════════════
        //  建立預設資產
        // ════════════════════════════════════════════════════════════

        private const string AssetPath = "Assets/遊戲素材/SO/Boss/GreedBossProfile.asset";
        private const string AnimFolder = "Assets/遊戲素材/敵人/貪/Animation";
        private const string ControllerPath = "Assets/遊戲素材/敵人/貪/GreedBoss.controller";

        [MenuItem("PilgrimOfSin/貪/建立預設 Boss 素材總表")]
        public static void CreateDefault()
        {
            if (AssetDatabase.LoadAssetAtPath<GreedBossProfile>(AssetPath) != null)
            {
                Debug.LogWarning($"[GreedBossProfile] {AssetPath} 已存在，不覆蓋。");
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<GreedBossProfile>(AssetPath);
                return;
            }

            var p = CreateInstance<GreedBossProfile>();
            p.displayName = "心魔-貪";
            p.maxHp = 5000f;
            p.moveSpeed = 4f;
            p.moveAnimReferenceSpeed = 4.5f;
            p.idle.stateName = "Idle";
            p.move.stateName = "Run";
            p.hurt.stateName = "Damage";
            p.dead.stateName = "Death";
            p.staggerDuration = 0f;
            p.animatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);

            // 命中區間依動畫裡翅膀揮最快的時間點抓的，可再微調。
            var a1 = new GreedMoveEntry
            {
                stateName = "Attack1", clip = Clip("Attack1"), canOpen = true, openerWeight = 2f,
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
                stateName = "Attack2", clip = Clip("Attack2"), canOpen = true, openerWeight = 1f, cooldown = 3f,
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
                stateName = "Attack3", clip = Clip("Attack3"), canOpen = true, openerWeight = 1f, cooldown = 4f,
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

            p.aerialKick.fly.clip = Clip("Fly");
            p.aerialKick.kick.clip = Clip("Kick");
            p.aerialKick.dive.clip = Clip("Dive");
            p.aerialKick.fall.clip = Clip("Fall");

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
