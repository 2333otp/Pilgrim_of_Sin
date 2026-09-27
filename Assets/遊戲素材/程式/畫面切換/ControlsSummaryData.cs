using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PilgrimOfSin
{
    /// <summary>
    /// 「操作說明」總覽清單資料：一份 asset 存所有項目（說明文字 + 對應 Action），
    /// 用於 ESC 選單「操作說明」畫面的左側列表，依序生成一行行的文字+按鍵圖示。
    /// </summary>
    [CreateAssetMenu(fileName = "ControlsSummaryData", menuName = "Pilgrim of Sin/UI/操作說明總覽資料 (ControlsSummaryData)")]
    public class ControlsSummaryData : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("說明文字，例如「鏡頭重置」「輕攻擊」")]
            public string label;

            [Tooltip("對應 PlayerInputActions 裡的 Action")]
            public InputActionReference action;

            [Tooltip("圖示顯示縮放倍率，預設1。少數素材本身留白較多、圖案偏小，可以調大讓玩家看得更清楚")]
            public float iconScale = 1f;
        }

        [SerializeField] private List<Entry> _entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries => _entries;
    }
}
