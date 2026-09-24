using TMPro;
using UnityEngine;

namespace PilgrimOfSin
{
    /// <summary>「操作說明」總覽清單的單一行：固定寬度文字 + 對齊同一條線的按鍵圖示。</summary>
    public class ControlsSummaryRowUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text _label;
        [SerializeField] private GlyphHighlightIcon _icon;

        public void SetData(ControlsSummaryData.Entry entry, InputGlyphDatabase glyphDatabase)
        {
            if (_label != null) _label.text = entry.label;
            if (_icon != null) _icon.Setup(entry.action, glyphDatabase);
        }
    }
}
