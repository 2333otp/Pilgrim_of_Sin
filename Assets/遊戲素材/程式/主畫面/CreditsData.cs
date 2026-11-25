using System.Text;
using UnityEngine;

namespace PilgrimOfSin
{
    /// <summary>
    /// 「製作團隊、官方社群」與「借物表」共用文字資料。
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

        [Header("借物表")]
        [Tooltip("借物表標題，ESC 選單每一頁頂端都會顯示；結局跑馬燈只顯示一次")]
        [SerializeField] private string _borrowedAssetsTitle = "借物表";

        [Tooltip("借物表內容（不含標題）。ESC 選單接在製作團隊頁之後、一個元素一頁；" +
                 "結局跑馬燈接在製作團隊名單下方連續捲動。可用 TMP Rich Text（例如 <color>）")]
        [SerializeField] private CreditsPage[] _borrowedAssetsPages = new CreditsPage[0];

        private int TeamPageCount => _pages?.Length ?? 0;
        private int BorrowedPageCount => _borrowedAssetsPages?.Length ?? 0;

        /// <summary>ESC 選單總頁數 = 製作團隊頁 + 借物表頁</summary>
        public int PageCount => TeamPageCount + BorrowedPageCount;

        public string GetPage(int index)
        {
            if (index < 0) return string.Empty;
            if (index < TeamPageCount) return _pages[index]?.text ?? string.Empty;

            int b = index - TeamPageCount;
            if (b >= BorrowedPageCount) return string.Empty;
            string body = _borrowedAssetsPages[b]?.text ?? string.Empty;
            return string.IsNullOrEmpty(_borrowedAssetsTitle) ? body : _borrowedAssetsTitle + "\n\n" + body;
        }

        /// <summary>
        /// 結局跑馬燈用：製作團隊頁依序串接（頁與頁之間留空行），
        /// 名單結束後空一大段再接借物表（標題只出現一次，各段之間空一行）。
        /// </summary>
        public string BuildRollText()
        {
            var sb = new StringBuilder();

            for (int i = 0; i < TeamPageCount; i++)
            {
                if (_pages[i] == null || string.IsNullOrEmpty(_pages[i].text)) continue;
                if (sb.Length > 0) sb.Append("\n\n\n\n");
                sb.Append(_pages[i].text);
            }

            bool titleWritten = false;
            for (int i = 0; i < BorrowedPageCount; i++)
            {
                var page = _borrowedAssetsPages[i];
                if (page == null || string.IsNullOrEmpty(page.text)) continue;

                if (!titleWritten)
                {
                    // 名單與借物表之間留比頁間距更大的空白，視覺上區隔成兩個段落
                    if (sb.Length > 0) sb.Append("\n\n\n\n\n\n\n");
                    if (!string.IsNullOrEmpty(_borrowedAssetsTitle))
                        sb.Append(_borrowedAssetsTitle).Append("\n\n");
                    titleWritten = true;
                }
                else
                {
                    sb.Append("\n\n");
                }
                sb.Append(page.text);
            }

            return sb.ToString();
        }
    }
}
