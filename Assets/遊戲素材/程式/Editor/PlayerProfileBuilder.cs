#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PilgrimOfSin.StateMachine.EditorTools
{
    /// <summary>
    /// 一鍵建立主角素材總表的預設資產：從 Muir_Gameplay.controller 讀出每個 state 綁的 Clip，
    /// 連同原本寫死在程式裡的保險時間、翻滾 / 受擊硬直餘裕一起填進去，行為與改版前一致。
    /// 資產已存在就不覆蓋（避免洗掉已經設定好的音效 / 特效）。
    /// </summary>
    public static class PlayerProfileBuilder
    {
        private const string ControllerPath = "Assets/遊戲素材/角色/謬爾/Muir_Gameplay.controller";
        private const string Folder = "Assets/遊戲素材/SO/主角";
        private static readonly string[] Suffixes = { "Pencil", "Brush", "PaintKnife", "Palette" };

        // 改版前寫死在各狀態裡的保險時間
        private static readonly Dictionary<PlayerActionId, float> Fallbacks = new Dictionary<PlayerActionId, float>
        {
            { PlayerActionId.LightAttack, 2.0f },
            { PlayerActionId.HeavyAttack, 2.9f },
            { PlayerActionId.Combo1, 6.5f }, { PlayerActionId.Combo2, 6.5f },
            { PlayerActionId.Combo3, 6.5f }, { PlayerActionId.Combo4, 6.5f },
            { PlayerActionId.SpecialSkill, 5.4f },
            { PlayerActionId.WeaponSwitch, 2.2f },
        };

        // 改版前 PlayerController 的固定秒數（Roll / Damaged 動畫長 1.167s，原本留餘裕到 1.2s）
        private const float OldRollDuration = 1.2f;
        private const float OldStunDuration = 1.2f;

        [MenuItem("PilgrimOfSin/主角/建立預設素材總表")]
        public static void Build()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) { Debug.LogError($"[PlayerProfileBuilder] 找不到 {ControllerPath}"); return; }

            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/遊戲素材/SO", "主角");

            var states = new Dictionary<string, AnimationClip>();
            foreach (var cs in controller.layers[0].stateMachine.states)
                states[cs.state.name] = cs.state.motion as AnimationClip; // BlendTree 會是 null

            var sets = new WeaponActionSet[4];
            for (int i = 0; i < 4; i++)
            {
                string path = $"{Folder}/WeaponActions_{Suffixes[i]}.asset";
                var set = AssetDatabase.LoadAssetAtPath<WeaponActionSet>(path);
                if (set == null)
                {
                    set = ScriptableObject.CreateInstance<WeaponActionSet>();
                    set.weaponSuffix = Suffixes[i];
                    foreach (PlayerActionId id in System.Enum.GetValues(typeof(PlayerActionId)))
                    {
                        if (id == PlayerActionId.Roll || id == PlayerActionId.Death) continue; // 共用動作
                        set.actions.Add(MakeEntry(id, id + "_" + Suffixes[i], states));
                    }
                    AssetDatabase.CreateAsset(set, path);
                }
                sets[i] = set;
            }

            string profilePath = $"{Folder}/PlayerProfile.asset";
            var profile = AssetDatabase.LoadAssetAtPath<PlayerProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<PlayerProfile>();
                profile.pencil = sets[0];
                profile.brush = sets[1];
                profile.paintKnife = sets[2];
                profile.palette = sets[3];
                profile.sharedActions.Add(MakeEntry(PlayerActionId.Roll, "Roll", states));
                profile.sharedActions.Add(MakeEntry(PlayerActionId.Death, "Death", states));
                AssetDatabase.CreateAsset(profile, profilePath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[PlayerProfileBuilder] 完成：{profilePath}", profile);
        }

        private static PlayerActionEntry MakeEntry(PlayerActionId id, string stateName, Dictionary<string, AnimationClip> states)
        {
            var entry = new PlayerActionEntry { stateName = stateName, action = id };
            if (states.TryGetValue(stateName, out var clip) && clip != null)
            {
                entry.clip = clip;
                entry.baseClip = clip;
            }
            if (Fallbacks.TryGetValue(id, out float fb)) entry.fallbackDuration = fb;
            if (entry.clip != null)
            {
                if (id == PlayerActionId.Roll) entry.endPadding = Mathf.Max(0f, OldRollDuration - entry.clip.length);
                else if (id == PlayerActionId.Damaged) entry.endPadding = Mathf.Max(0f, OldStunDuration - entry.clip.length);
            }
            return entry;
        }
    }
}
#endif
