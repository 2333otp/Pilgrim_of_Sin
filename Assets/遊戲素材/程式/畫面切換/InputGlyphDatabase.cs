using System;
using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin
{
    /// <summary>
    /// 輸入圖示資料庫：把 Input System 的 Control Path（例如 &lt;Gamepad&gt;/buttonSouth、
    /// &lt;Keyboard&gt;/space）對應到實際要顯示的按鍵圖示。手把、鍵盤各自一份清單，
    /// 鍵盤素材補齊前，清單留著空白項目即可，不影響手把已經能用的部分。
    /// </summary>
    [CreateAssetMenu(fileName = "InputGlyphDatabase", menuName = "Pilgrim of Sin/UI/輸入圖示資料庫 (InputGlyphDatabase)")]
    public class InputGlyphDatabase : ScriptableObject
    {
        [Serializable]
        public class GlyphEntry
        {
            [Tooltip("Input System 的 Control Path，例如 <Gamepad>/buttonSouth")]
            public string controlPath;
            public Sprite sprite;

            [Tooltip("按下時要顯示的圖（例如桃紅色高亮版），沒有的話留空即可")]
            public Sprite pressedSprite;
        }

        [Header("手把按鍵圖示")]
        [SerializeField] private List<GlyphEntry> _gamepadEntries = new List<GlyphEntry>();

        [Header("鍵盤按鍵圖示（素材補齊前可留空）")]
        [SerializeField] private List<GlyphEntry> _keyboardEntries = new List<GlyphEntry>();

        public Sprite GetSprite(string controlPath)
        {
            if (string.IsNullOrEmpty(controlPath)) return null;
            return Find(_gamepadEntries, controlPath)?.sprite ?? Find(_keyboardEntries, controlPath)?.sprite;
        }

        /// <summary>取得按下時的高亮圖示；沒有設定的話 fallback 回一般圖示。</summary>
        public Sprite GetPressedSprite(string controlPath)
        {
            if (string.IsNullOrEmpty(controlPath)) return null;
            var entry = Find(_gamepadEntries, controlPath) ?? Find(_keyboardEntries, controlPath);
            if (entry == null) return null;
            return entry.pressedSprite != null ? entry.pressedSprite : entry.sprite;
        }

        private static GlyphEntry Find(List<GlyphEntry> entries, string controlPath)
        {
            foreach (var entry in entries)
                if (string.Equals(entry.controlPath, controlPath, StringComparison.OrdinalIgnoreCase))
                    return entry;
            return null;
        }
    }
}
