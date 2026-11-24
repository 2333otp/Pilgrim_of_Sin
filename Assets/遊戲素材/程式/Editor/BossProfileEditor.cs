#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PilgrimOfSin.StateMachine.EditorTools
{
    /// <summary>
    /// 所有 Boss 素材總表（BossProfileBase）共用的 Inspector：
    ///   共通欄位 → 各 Boss 專屬欄位 → 測試 / 音訊輸出，最下面是「從 Animator 讀取原始動畫」按鈕。
    /// 貪另外有自己的 GreedBossProfileEditor（繼承這個，多了招式檢查與同步到 Animator）。
    /// </summary>
    [CustomEditor(typeof(BossProfileBase), true)]
    public class BossProfileEditor : Editor
    {
        private static readonly string[] Tail = { "showHitArea", "mixerGroup" };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var excluded = new List<string> { "m_Script" };
            excluded.AddRange(Tail);
            DrawPropertiesExcluding(serializedObject, excluded.ToArray());
            foreach (var name in Tail)
            {
                var p = serializedObject.FindProperty(name);
                if (p != null) EditorGUILayout.PropertyField(p, true);
            }
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(10);
            DrawTools((BossProfileBase)target);
        }

        /// <summary>Inspector 最下面的工具區。子類別可以覆寫加自己的按鈕 / 檢查。</summary>
        protected virtual void DrawTools(BossProfileBase p)
        {
            foreach (var msg in ValidateStates(p))
                EditorGUILayout.HelpBox(msg, MessageType.Warning);

            using (new EditorGUI.DisabledScope(p.animatorController == null))
            {
                if (GUILayout.Button("從 Animator 讀取原始動畫（填入原始 Clip；有換過的動畫和音效 / 特效不會被清掉）", GUILayout.Height(30)))
                    SyncBaseClips(p);
            }
            if (p.animatorController == null)
                EditorGUILayout.HelpBox("請先在「動畫」指定這隻 Boss 的 Animator Controller。", MessageType.Info);
        }

        /// <summary>Animator 第一層（含子狀態機）裡所有 state：名稱 → 綁的動畫。</summary>
        public static Dictionary<string, AnimatorState> CollectStates(RuntimeAnimatorController rac)
        {
            var map = new Dictionary<string, AnimatorState>();
            var ac = rac as AnimatorController;
            if (ac == null || ac.layers.Length == 0) return map;
            void Walk(AnimatorStateMachine sm)
            {
                foreach (var cs in sm.states) map[cs.state.name] = cs.state;
                foreach (var sub in sm.stateMachines) Walk(sub.stateMachine);
            }
            Walk(ac.layers[0].stateMachine);
            return map;
        }

        /// <summary>每個動作的 state 名稱是否都在 Animator 裡。</summary>
        public static List<string> ValidateStates(BossProfileBase p)
        {
            var list = new List<string>();
            if (p.animatorController == null) return list;
            var states = CollectStates(p.animatorController);
            foreach (var e in p.AllEntries())
            {
                if (e == null || string.IsNullOrEmpty(e.stateName)) continue;
                if (!states.ContainsKey(e.stateName))
                    list.Add($"「{e.stateName}」在 Animator Controller 裡找不到，這個動作的動畫替換不會生效。");
                else if (e.clip != null && e.baseClip == null)
                    list.Add($"「{e.stateName}」還沒有原始 Clip，請按下面的按鈕讀取（否則換的動畫不會生效）。");
            }
            return list;
        }

        /// <summary>
        /// 從 Animator 讀出每個動作原本綁的 Clip，填進 baseClip。
        /// 動畫欄位是空的、或沒換過（等於舊的原始 Clip）時，一起更新成 Animator 的；換過的保留。
        /// </summary>
        public static void SyncBaseClips(BossProfileBase p)
        {
            var states = CollectStates(p.animatorController);
            Undo.RecordObject(p, "Sync Boss Base Clips");
            int updated = 0;
            var missing = new List<string>();
            foreach (var e in p.AllEntries())
            {
                if (e == null || string.IsNullOrEmpty(e.stateName)) continue;
                if (!states.TryGetValue(e.stateName, out var st)) { missing.Add(e.stateName); continue; }
                var baseClip = st.motion as AnimationClip;
                if (baseClip == null) { missing.Add(e.stateName + "（不是單一 Clip）"); continue; }
                if (e.clip == null || e.clip == e.baseClip) e.clip = baseClip;
                if (e.baseClip != baseClip) { e.baseClip = baseClip; updated++; }
            }
            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
            Debug.Log($"[{p.name}] 已從 Animator 讀取原始動畫：更新 {updated} 筆。" +
                      (missing.Count > 0 ? $"找不到 / 無法讀取：{string.Join("、", missing)}" : ""));
        }
    }
}
#endif
