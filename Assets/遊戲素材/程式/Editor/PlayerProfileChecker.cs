#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PilgrimOfSin.StateMachine.EditorTools
{
    /// <summary>
    /// 檢查主角素材總表（PlayerProfile）每一筆動作能不能正常替換動畫，結果印在 Console。
    /// 會順手把「過期的 baseClip」（Animator Controller 裡該 state 現在綁的 Clip 與 SO 記的不同）自動修正，
    /// 不會動你放的 clip。
    /// </summary>
    public static class PlayerProfileChecker
    {
        private const string ControllerPath = "Assets/遊戲素材/角色/謬爾/Muir_Gameplay.controller";
        private const string ProfilePath = "Assets/遊戲素材/SO/主角/PlayerProfile.asset";

        [MenuItem("PilgrimOfSin/主角/檢查素材總表")]
        public static void Check()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            var profile = AssetDatabase.LoadAssetAtPath<PlayerProfile>(ProfilePath);
            if (controller == null || profile == null)
            {
                Debug.LogError($"[PlayerProfileChecker] 找不到 Controller 或 PlayerProfile：{ControllerPath} / {ProfilePath}");
                return;
            }

            var states = new Dictionary<string, Motion>();
            foreach (var cs in controller.layers[0].stateMachine.states)
                states[cs.state.name] = cs.state.motion;

            var report = new StringBuilder();
            int swapped = 0, problems = 0, fixedCount = 0;

            foreach (var e in profile.AllEntries())
            {
                if (string.IsNullOrEmpty(e.stateName)) continue;

                if (!states.TryGetValue(e.stateName, out var motion))
                {
                    if (e.clip != null) { report.AppendLine($"✗ {e.stateName}：Animator Controller 裡沒有這個 state，放的 Clip 不會生效。"); problems++; }
                    continue;
                }

                if (motion is BlendTree)
                {
                    if (e.clip != null) { report.AppendLine($"✗ {e.stateName}：這個 state 是 BlendTree，不能用單一 Clip 替換（要直接改 Controller 裡的 BlendTree）。"); problems++; }
                    continue;
                }

                var controllerClip = motion as AnimationClip;
                if (controllerClip == null) continue;

                if (e.baseClip != controllerClip)
                {
                    // 原本沒填，或 Controller 改過之後 SO 沒跟上：以 Controller 現在的為準
                    if (e.clip == null || e.clip == e.baseClip) e.clip = controllerClip;
                    e.baseClip = controllerClip;
                    fixedCount++;
                    report.AppendLine($"△ {e.stateName}：baseClip 已修正為 Controller 目前綁的 {controllerClip.name}。");
                }

                if (e.clip == null || e.clip == e.baseClip) continue;

                swapped++;
                var s = AnimationUtility.GetAnimationClipSettings(e.clip);
                if (!s.loopBlendPositionY)
                {
                    report.AppendLine($"△ {e.stateName}：替換的 Clip「{e.clip.name}」沒勾 Root Transform Position (Y) 的 Bake Into Pose，" +
                                      "可能造成遁地（程式已忽略 Y 位移，但視覺高度仍可能偏移）。原本的動畫都有勾。");
                    problems++;
                }
                if (s.heightFromFeet)
                    report.AppendLine($"△ {e.stateName}：替換的 Clip「{e.clip.name}」Based Upon (at Start) 選了 Feet，原本的動畫是 Original，高度可能不同。");
            }

            if (fixedCount > 0)
            {
                EditorUtility.SetDirty(profile);
                foreach (var set in new[] { profile.pencil, profile.brush, profile.paintKnife, profile.palette })
                    if (set != null) EditorUtility.SetDirty(set);
                AssetDatabase.SaveAssets();
            }

            string summary = $"[PlayerProfileChecker] 已替換 {swapped} 筆；發現問題 {problems} 筆；自動修正 baseClip {fixedCount} 筆。\n" +
                             (report.Length > 0 ? report.ToString() : "沒有發現問題。");
            Debug.Log(summary, profile);
        }
    }
}
#endif
