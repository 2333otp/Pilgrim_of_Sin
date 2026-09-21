using System.Text;
using UnityEngine;

namespace PilgrimOfSin
{
    /// <summary>
    /// 「製作團隊、官方社群」共用文字資料。
    /// ESC 選單的 CreditsPageBook（分頁顯示）與結局的 EndingCreditsRoll（跑馬燈捲動）
    /// 都讀取同一份資料，只是呈現方式不同，之後只需要改這份資料。
    /// </summary>
    [CreateAssetMenu(fileName = "CreditsData", menuName = "PilgrimOfSin/Credits Data")]
    public class CreditsData : ScriptableObject
    {
        [System.Serializable]
        public class CreditsPage
        {
            [TextArea(3, 12)] public string text;
        }

        [Tooltip("依序：第1頁～第4頁，跟現有 ESC 選單製作團隊分頁順序一致")]
        [SerializeField] private CreditsPage[] _pages = new CreditsPage[4];

        public int PageCount => _pages?.Length ?? 0;

        public string GetPage(int index)
            => (_pages != null && index >= 0 && index < _pages.Length) ? _pages[index].text : string.Empty;

        /// <summary>結局跑馬燈用：所有頁面文字依序串接，頁與頁之間留空行分隔。</summary>
        public string BuildRollText()
        {
            if (_pages == null || _pages.Length == 0) return string.Empty;

            var sb = new StringBuilder();
            for (int i = 0; i < _pages.Length; i++)
            {
                if (_pages[i] == null || string.IsNullOrEmpty(_pages[i].text)) continue;
                sb.Append(_pages[i].text);
                if (i < _pages.Length - 1) sb.Append("\n\n\n\n");
            }
            return sb.ToString();
        }
    }
}
