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

            DrawComboMoveSet(serializedObject.FindProperty("combo"));
            DrawHitWindowButtons();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("動作清單（舊連段、空中攻擊、受擊等）", EditorStyles.largeLabel);

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

        // ── 出招表（無雙式連段研究） ──────────────────────────────────
        private static readonly (string field, string label)[] ComboSlots =
        {
            ("n1", "N1  ⬜"), ("n2", "N2  ⬜⬜"), ("n3", "N3  ⬜⬜⬜"), ("n4", "N4  ⬜⬜⬜⬜"),
            ("c1", "C1  🔺"), ("c2", "C2  ⬜🔺"), ("c3", "C3  ⬜⬜🔺"), ("c4", "C4  ⬜⬜⬜🔺"),
            ("pauseCombo", "停頓連打  ⬜⬜（停）⬜"), ("chargeHold", "蓄力  🔺長按"),
        };

        private void DrawHitWindowButtons()
        {
            EditorGUILayout.Space(4);
            if (GUILayout.Button("開啟攻擊判定編輯器（在 Scene 視窗拖拉判定形狀）")) HitboxEditorWindow.Open();
            using (new EditorGUILayout.HorizontalScope())
            {
                bool fillEmpty = GUILayout.Button("自動抓命中區間（只填還沒設的招）");
                bool overwrite = GUILayout.Button("全部重抓");
                if (!fillEmpty && !overwrite) return;
                if (overwrite && !EditorUtility.DisplayDialog("自動抓命中區間",
                        "這把武器所有招式的命中區間都會重抓，手動調過的會被蓋掉。確定嗎？", "全部重抓", "取消"))
                    return;

                serializedObject.ApplyModifiedProperties();
                Debug.Log(ComboHitWindowAnalyzer.RunOne((WeaponActionSet)target, overwrite));
                serializedObject.Update();
                GUIUtility.ExitGUI();
            }
        }

        private static void DrawComboMoveSet(SerializedProperty combo)
        {
            if (combo == null) return;

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("出招表（無雙式連段，研究中）", EditorStyles.largeLabel);
            EditorGUILayout.HelpBox(
                "勾選「使用出招表」後，地面攻擊改用下面的招式；空中攻擊仍用下方動作清單。\n" +
                "放哪段動畫就播哪段，動作長度 = 動畫長度 ÷ 播放速度。\n" +
                "接收開始：播到這個比例之後按的鍵才算數，只認第一個（⬜ / 🔺 / 翻滾）。\n" +
                "取消時間點：最早能出下一招的時間；設 1 = 播完才接。\n" +
                "停頓連打：在指定那一下 ⬜ 的取消時間點之後（到播完 + 寬限）才按 ⬜；更早按是一般連段。\n" +
                "蓄力：🔺 按住超過門檻；更早放開出原本的強攻擊。沒放動畫的分支招不會啟用。\n" +
                "命中區間：每一格是一下揮擊，各算一次傷害；留空 = 整招只算一下。可用下方按鈕自動抓。",
                MessageType.Info);

            EditorGUILayout.PropertyField(combo.FindPropertyRelative("enabled"), new GUIContent("使用出招表"));

            for (int i = 0; i < ComboSlots.Length; i++)
            {
                var (field, label) = ComboSlots[i];
                // 分組標題（一般攻擊／強攻擊／惡魔獵人分支）由 ComboMoveSet 欄位上的 [Header] 自動畫出
                var move = combo.FindPropertyRelative(field);
                if (move == null) continue;
                EditorGUILayout.PropertyField(move, new GUIContent(MoveTitle(label, move)), true);

                if (field == "pauseCombo")
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(combo.FindPropertyRelative("pauseAfterNormal"), new GUIContent("接在第幾下 ⬜ 之後"));
                    EditorGUILayout.PropertyField(combo.FindPropertyRelative("pauseGraceSeconds"), new GUIContent("停頓寬限（秒）"));
                    EditorGUI.indentLevel--;
                }
                else if (field == "chargeHold")
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(combo.FindPropertyRelative("chargeHoldSeconds"), new GUIContent("長按門檻（秒）"));
                    EditorGUI.indentLevel--;
                }
            }

            var special = combo.FindPropertyRelative("specialHitWindows");
            if (special != null)
            {
                string title = special.arraySize > 0 ? $"秘劍 R1 命中區間　命中 {special.arraySize} 下" : "秘劍 R1 命中區間　整招一下";
                EditorGUILayout.PropertyField(special, new GUIContent(title), true);
            }
        }

        /// <summary>收合時的標題：「N1 ⬜　combo2_pencil_s1 · 0.79s · 取消 50%」，沒放動畫顯示（空）。</summary>
        private static string MoveTitle(string label, SerializedProperty move)
        {
            var clip = move.FindPropertyRelative("clip").objectReferenceValue as AnimationClip;
            if (clip == null) return $"{label}　（空）";
            float speed = Mathf.Max(0.1f, move.FindPropertyRelative("speed").floatValue);
            float cancel = move.FindPropertyRelative("cancelRatio").floatValue;
            float input = move.FindPropertyRelative("inputStartRatio").floatValue;
            var windows = move.FindPropertyRelative("hitWindows");
            string hits = windows != null && windows.arraySize > 0 ? $"命中 {windows.arraySize} 下" : "整招一下";
            return $"{label}　{clip.name} · {clip.length / speed:F2}s · 接收 {input * 100f:F0}% · 取消 {cancel * 100f:F0}% · {hits}";
        }
    }
}
#endif
