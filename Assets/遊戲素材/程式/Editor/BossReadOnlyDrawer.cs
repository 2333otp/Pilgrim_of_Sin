#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace PilgrimOfSin.StateMachine.EditorTools
{
    /// <summary>[BossReadOnly]：欄位照常顯示但不能改（灰色）。</summary>
    [CustomPropertyDrawer(typeof(BossReadOnlyAttribute))]
    public class BossReadOnlyDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
            => EditorGUI.GetPropertyHeight(property, label, true);

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUI.PropertyField(position, property, label, true);
        }
    }
}
#endif
