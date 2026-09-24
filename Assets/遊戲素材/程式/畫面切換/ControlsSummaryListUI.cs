using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin
{
    /// <summary>
    /// 「操作說明」總覽清單管理器：讀取 ControlsSummaryData，依序生成每一行(文字+按鍵圖示)。
    /// 掛在清單容器的父物件上，物件啟用時才會生成一次；面板關閉時底下的 GlyphHighlightIcon
    /// 會各自透過 OnDisable 自動停止監聽按鍵輸入。
    /// </summary>
    public class ControlsSummaryListUI : MonoBehaviour
    {
        [SerializeField] private ControlsSummaryData _data;
        [SerializeField] private InputGlyphDatabase _glyphDatabase;
        [SerializeField] private ControlsSummaryRowUI _rowPrefab;
        [SerializeField] private Transform _rowContainer;

        private readonly List<ControlsSummaryRowUI> _spawnedRows = new List<ControlsSummaryRowUI>();

        private void Awake()
        {
            BuildRows();
        }

        private void BuildRows()
        {
            if (_data == null || _rowPrefab == null || _rowContainer == null) return;
            if (_spawnedRows.Count > 0) return; // 已經生成過就不重複建立

            foreach (var entry in _data.Entries)
            {
                var row = Instantiate(_rowPrefab, _rowContainer);
                row.SetData(entry, _glyphDatabase);
                _spawnedRows.Add(row);
            }
        }
    }
}
