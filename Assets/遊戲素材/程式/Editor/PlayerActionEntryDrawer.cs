#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PilgrimOfSin.StateMachine.EditorTools
{
    /// <summary>
    /// 動作素材的 Inspector 顯示：標題用中文動作名 + 動畫名與長度；
    /// 只露出動畫 / 音效 / 配音 / 特效，程式用的系統欄位收進最下面的「進階」摺疊。
    /// </summary>
    [CustomPropertyDrawer(typeof(PlayerActionEntry))]
    public class PlayerActionEntryDrawer : PropertyDrawer
    {
        private static readonly (string field, string label)[] Main =
        {
            ("clip", "動畫 Clip"),
            ("sfx", "音效（可直接拖音檔，隨機播一個）"),
            ("sfxVolume", "音效音量"),
            ("sfxNormalizedTime", "音效時間點 (0~1)"),
            ("voiceEnglish", "配音 - 英文（隨機播一個）"),
            ("voiceTaiwanese", "配音 - 台語（隨機播一個）"),
            ("voiceVolume", "配音音量"),
            ("voiceNormalizedTime", "配音時間點 (0~1)"),
            ("vfx", "特效"),
        };

        private static readonly (string field, string label)[] Advanced =
        {
            ("stateName", "Animator state 名稱"),
            ("action", "動作種類"),
            ("baseClip", "原始 Clip（自動填入）"),
            ("endPadding", "結尾餘裕（秒）"),
            ("fallbackDuration", "保險時間（秒）"),
        };

        private static readonly HashSet<string> AdvancedOpen = new HashSet<string>();
        private const float Gap = 2f;

        public static string ActionLabel(PlayerActionId id) => id switch
        {
            PlayerActionId.Idle => "待機",
            PlayerActionId.Locomotion => "移動",
            PlayerActionId.LightAttack => "輕攻擊",
            PlayerActionId.HeavyAttack => "重攻擊",
            PlayerActionId.Combo1 => "連段 1",
            PlayerActionId.Combo2 => "連段 2",
            PlayerActionId.Combo3 => "連段 3",
            PlayerActionId.Combo4 => "連段 4",
            PlayerActionId.SpecialSkill => "特殊技能",
            PlayerActionId.WeaponSwitch => "切換武器",
            PlayerActionId.Damaged => "受擊",
            PlayerActionId.Jump => "跳躍",
            PlayerActionId.Fall => "下落",
            PlayerActionId.Roll => "翻滾",
            PlayerActionId.Death => "死亡",
            _ => id.ToString(),
        };

        private static string Title(SerializedProperty property)
        {
            var action = property.FindPropertyRelative("action");
            var clip = property.FindPropertyRelative("clip").objectReferenceValue as AnimationClip;
            string name = ActionLabel((PlayerActionId)action.enumValueIndex);
            return clip != null ? $"{name}　（{clip.name} · {clip.length:0.00}s）" : $"{name}　（無動畫）";
        }

        private static string AdvancedKey(SerializedProperty p)
            => p.serializedObject.targetObject.GetInstanceID() + p.propertyPath;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float h = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded) return h;

            foreach (var (field, _) in Main)
                h += Gap + EditorGUI.GetPropertyHeight(property.FindPropertyRelative(field), true);

            h += Gap + EditorGUIUtility.singleLineHeight; // 進階摺疊
            if (AdvancedOpen.Contains(AdvancedKey(property)))
                foreach (var (field, _) in Advanced)
                    h += Gap + EditorGUI.GetPropertyHeight(property.FindPropertyRelative(field), true);
            return h;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, Title(property), true);

            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                float y = line.yMax + Gap;

                foreach (var (field, text) in Main)
                    y = DrawField(position, y, property.FindPropertyRelative(field), text);

                string key = AdvancedKey(property);
                bool open = AdvancedOpen.Contains(key);
                var advLine = new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
                bool nowOpen = EditorGUI.Foldout(advLine, open, "進階（程式用，一般不用改）", true);
                if (nowOpen != open) { if (nowOpen) AdvancedOpen.Add(key); else AdvancedOpen.Remove(key); }
                y = advLine.yMax + Gap;

                if (nowOpen)
                    foreach (var (field, text) in Advanced)
                        y = DrawField(position, y, property.FindPropertyRelative(field), text);

                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }

        private static float DrawField(Rect position, float y, SerializedProperty p, string labelText)
        {
            float h = EditorGUI.GetPropertyHeight(p, true);
            var tip = p.tooltip;
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, h), p, new GUIContent(labelText, tip), true);
            return y + h + Gap;
        }
    }

    /// <summary>
    /// 武器動作集的 Inspector：依「移動 → 攻擊 → 連段 → 特殊技能 → 切換武器 → 受擊」分組排列，
    /// 待機 / 移動（BlendTree）不顯示。
    /// </summary>
    [CustomEditor(typeof(WeaponActionSet))]
    public class WeaponActionSetEditor : Editor
    {
        private static readonly (string title, PlayerActionId[] ids)[] Groups =
        {
            ("跳躍 / 下落", new[] { PlayerActionId.Jump, PlayerActionId.Fall }),
            ("攻擊", new[] { PlayerActionId.LightAttack, PlayerActionId.HeavyAttack }),
            ("連段", new[] { PlayerActionId.Combo1, PlayerActionId.Combo2, PlayerActionId.Combo3, PlayerActionId.Combo4 }),
            ("特殊技能", new[] { PlayerActionId.SpecialSkill }),
            ("切換武器", new[] { PlayerActionId.WeaponSwitch }),
            ("受擊", new[] { PlayerActionId.Damaged }),
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("weaponSuffix"),
                new GUIContent("武器後綴（程式用）"));

            var actions = serializedObject.FindProperty("actions");

            foreach (var (title, ids) in Groups)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                foreach (var id in ids)
                {
                    for (int i = 0; i < actions.arraySize; i++)
                    {
                        var element = actions.GetArrayElementAtIndex(i);
                        if (element.FindPropertyRelative("action").enumValueIndex != (int)id) continue;
                        EditorGUILayout.PropertyField(element, true);
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
