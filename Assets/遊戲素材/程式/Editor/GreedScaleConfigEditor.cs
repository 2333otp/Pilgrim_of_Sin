#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PilgrimOfSin.StateMachine.EditorTools
{
    /// <summary>
    /// GreedScaleConfig 的 Inspector：「從 Animator 讀取 state」按鈕 + 設定檢查。
    /// </summary>
    [CustomEditor(typeof(GreedScaleConfig))]
    public class GreedScaleConfigEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var cfg = (GreedScaleConfig)target;
            EditorGUILayout.Space(10);

            var ac = cfg.animatorController as AnimatorController;
            if (ac != null)
            {
                var names = new HashSet<string>(ac.layers[0].stateMachine.states.Select(s => s.state.name));
                foreach (var s in cfg.states)
                    if (s != null && !names.Contains(s.stateName))
                        EditorGUILayout.HelpBox($"「{s.stateName}」在 Animator Controller 裡找不到，這筆不會生效。", MessageType.Warning);
                foreach (var n in names)
                    if (cfg.Find(n) == null)
                        EditorGUILayout.HelpBox($"Animator 有 state「{n}」但清單裡沒有，按下面的按鈕補上。", MessageType.Info);
            }
            else
                EditorGUILayout.HelpBox("請先指定 Animator Controller（LibraScale.controller）。", MessageType.Info);

            using (new EditorGUI.DisabledScope(ac == null))
            {
                if (GUILayout.Button("從 Animator 讀取 state（補上缺的、更新原始動畫；已設定的音效 / 特效不會被清掉）", GUILayout.Height(30)))
                    SyncFromAnimator(cfg);
            }
        }

        public static void SyncFromAnimator(GreedScaleConfig cfg)
        {
            var ac = cfg.animatorController as AnimatorController;
            if (ac == null) return;
            Undo.RecordObject(cfg, "Sync Scale States");

            int added = 0;
            foreach (var cs in ac.layers[0].stateMachine.states)
            {
                var state = cs.state;
                var baseClip = state.motion as AnimationClip;
                var entry = cfg.Find(state.name);
                if (entry == null)
                {
                    entry = new ScaleAnimEntry { stateName = state.name, clip = baseClip };
                    cfg.states.Add(entry);
                    added++;
                }
                // 第一次填入，或使用者沒換過動畫（clip 還等於舊的 baseClip）時，跟著 Animator 更新
                if (entry.clip == null || entry.clip == entry.baseClip) entry.clip = baseClip;
                entry.baseClip = baseClip;
            }

            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();
            Debug.Log($"[GreedScaleConfig] 已從 Animator 讀取：新增 {added} 個 state，共 {cfg.states.Count} 個。");
        }
    }
}
#endif
