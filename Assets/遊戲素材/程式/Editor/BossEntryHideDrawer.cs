#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PilgrimOfSin.StateMachine.EditorTools
{
    /// <summary>
    /// [BossEntryHide] 的 Inspector：照常顯示 BossActionEntry（或子類別）的欄位，但跳過指定的欄位，
    /// 例如待機 / 移動 / 受擊 / 死亡不顯示攻擊判定。
    /// </summary>
    [CustomPropertyDrawer(typeof(BossEntryHideAttribute))]
    public class BossEntryHideDrawer : PropertyDrawer
    {
        private const float Gap = 2f;

        private HashSet<string> Hidden => new HashSet<string>(((BossEntryHideAttribute)attribute).Fields);

        private IEnumerable<SerializedProperty> Visible(SerializedProperty property)
        {
            var hidden = Hidden;
            var it = property.Copy();
            var end = property.GetEndProperty();
            bool enter = true;
            while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
            {
                enter = false;
                if (!hidden.Contains(it.name)) yield return it.Copy();
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.Generic) return EditorGUI.GetPropertyHeight(property, label, true);
            float h = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded) return h;
            foreach (var c in Visible(property)) h += EditorGUI.GetPropertyHeight(c, true) + Gap;
            return h;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.Generic) { EditorGUI.PropertyField(position, property, label, true); return; }

            EditorGUI.BeginProperty(position, label, property);
            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, label, true);
            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                float y = line.yMax + Gap;
                foreach (var c in Visible(property))
                {
                    float h = EditorGUI.GetPropertyHeight(c, true);
                    EditorGUI.PropertyField(new Rect(position.x, y, position.width, h), c, true);
                    y += h + Gap;
                }
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndProperty();
        }
    }
}
#endif
