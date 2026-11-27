using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace PilgrimOfSin
{
    /// <summary>
    /// 「角色資料、心魔圖鑑」兩頁書。玩家狀態面板的分頁內容之一，由 PauseMenuUI 開關。
    ///   第 1 頁（人物）：繆爾 / 萊恩 / 凡斯
    ///   第 2 頁（心魔）：阿貪 / 阿嗔 / 阿痴（照貪嗔痴關卡順序）
    /// 版面比照武器介紹第 1 頁：左欄清單、中間立繪（不畫框，直接站在背景上）、右欄名稱＋身份＋介紹。
    /// 操作（手把）：上下 = 選人（頭尾相接）；左右 = 翻頁。兩頁各自記得選到誰，關掉再打開才回到第一個。
    /// 返回由 PauseMenuUI 統一處理。
    /// 版面由 Editor 選單「PilgrimOfSin/UI/重建角色圖鑑版面」產生，數值以 1920×1080 為準。
    /// </summary>
    public class CharacterCodexPageBook : MonoBehaviour
    {
        [System.Serializable]
        public class CharEntry
        {
            [Tooltip("立繪（已裁掉透明邊、腳底已淡出；腳底對齊下方基準線）")]
            public Sprite art;
            [Tooltip("清單上顯示的名稱")]
            public string shortName;
            [Tooltip("右欄大標題")]
            public string charName;
            [Tooltip("名稱下方的小字身份，例：畫家、心魔・貪")]
            public string subtitle;
            [TextArea(2, 6)]
            [Tooltip("介紹文字。請照語意手動換行（Enter），避免系統自動斷行留下一兩個字的孤行。")]
            public string description;
            [Tooltip("個別微調：立繪放大倍率（在頁面上限算完之後再乘）")]
            public float artScale = 1f;
            [Tooltip("個別微調：立繪左右位移（1920 基準，正數往右）")]
            public float artOffsetX = 0f;
        }

        [System.Serializable]
        public class CodexPage
        {
            [Tooltip("頁碼列上的名稱，例：人物")]
            public string label;
            [Tooltip("底部提示列「選擇○○」用的提示資料")]
            public InputPromptData selectPrompt;
            [Tooltip("立繪最大高度（1920 基準）")]
            public float artMaxHeight = 750f;
            [Tooltip("立繪最大寬度（1920 基準）。立繪右緣不要超過右欄文字左邊 75 以內，不然會跟字黏在一起")]
            public float artMaxWidth = 600f;
            [Tooltip("立繪腳底中心點位置（相對畫面中心，1920 基準）")]
            public Vector2 artFootPosition = new Vector2(-75f, -396f);
            public CharEntry[] entries = new CharEntry[3];
        }

        [System.Serializable]
        public class CharRow
        {
            public Button button;
            [Tooltip("選中時左濃右淡的底色帶")] public Image band;
            [Tooltip("選中時左側的金色直線")] public Image bar;
            public TMP_Text marker;
            public TMP_Text label;
        }

        [Header("翻頁箭頭（第 1 頁顯示右箭頭、第 2 頁顯示左箭頭）")]
        [SerializeField] private Button _leftArrowBtn;
        [SerializeField] private Button _rightArrowBtn;
        [SerializeField] private TMP_Text _pageIndicatorText;

        [Header("清單")]
        [SerializeField] private CharRow[] _rows = new CharRow[3];

        [Header("內容顯示")]
        [SerializeField] private Image _artImage;
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private TextMeshProUGUI _subtitleText;
        [SerializeField] private TextMeshProUGUI _descriptionText;

        [Header("底部提示列：「選擇人物／選擇心魔」")]
        [SerializeField] private InputPromptUI _selectHint;

        [Header("清單顏色")]
        [SerializeField] private Color _rowTextOn = new Color32(0xF3, 0xDC, 0xA6, 0xFF);
        [SerializeField] private Color _rowTextOff = new Color32(0x8F, 0x84, 0x6E, 0xFF);

        [Header("頁面資料（第 1 頁人物、第 2 頁心魔）")]
        [SerializeField] private CodexPage[] _pages = new CodexPage[2];

        private int _page;
        private int[] _selected = new int[2];

        private void OnEnable()
        {
            // 關掉再打開：回到第 1 頁第一個人
            _page = 0;
            _selected = new int[Mathf.Max(1, _pages != null ? _pages.Length : 1)];
            RefreshAll();
        }

        private void Start()
        {
            if (_leftArrowBtn != null) _leftArrowBtn.onClick.AddListener(() => SetPage(0));
            if (_rightArrowBtn != null) _rightArrowBtn.onClick.AddListener(() => SetPage(1));
            for (int i = 0; i < _rows.Length; i++)
            {
                int idx = i;
                if (_rows[i]?.button != null) _rows[i].button.onClick.AddListener(() => Select(idx));
            }
        }

        private void Update()
        {
            var reader = PauseMenuUI.Instance != null ? PauseMenuUI.Instance.InputReader : null;
            if (reader == null) return;

            if (reader.MenuPageLeftPressed) { SetPage(0); return; }
            if (reader.MenuPageRightPressed) { SetPage(1); return; }

            int n = CurrentCount();
            if (n <= 0) return;
            if (reader.MenuUpPressed) Select((_selected[_page] - 1 + n) % n);
            else if (reader.MenuDownPressed) Select((_selected[_page] + 1) % n);
        }

        private int CurrentCount()
        {
            if (_pages == null || _page >= _pages.Length || _pages[_page]?.entries == null) return 0;
            return _pages[_page].entries.Length;
        }

        private void Select(int index)
        {
            int n = CurrentCount();
            if (n <= 0) return;
            _selected[_page] = Mathf.Clamp(index, 0, n - 1);
            RefreshAll();
        }

        private void SetPage(int page)
        {
            if (_pages == null || page < 0 || page >= _pages.Length || _page == page) return;
            _page = page;   // 不重設 _selected：翻回來還是停在剛剛選的人
            RefreshAll();
        }

        private void RefreshAll()
        {
            if (_pages == null || _pages.Length == 0) return;
            _page = Mathf.Clamp(_page, 0, _pages.Length - 1);
            var page = _pages[_page];
            int n = CurrentCount();
            int sel = n > 0 ? Mathf.Clamp(_selected[_page], 0, n - 1) : 0;
            var entry = n > 0 ? page.entries[sel] : null;

            if (_rightArrowBtn != null) _rightArrowBtn.gameObject.SetActive(_page < _pages.Length - 1);
            if (_leftArrowBtn != null) _leftArrowBtn.gameObject.SetActive(_page > 0);

            if (_pageIndicatorText != null)
            {
                const string on = "#D9B36A", off = "#8A7F6A";
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < _pages.Length; i++)
                {
                    if (i > 0) sb.Append("  <color=#7A6C52>｜</color>  ");
                    sb.Append($"<color={(i == _page ? on : off)}>{_pages[i]?.label}</color>");
                }
                sb.Append($"     <color=#B9AD94>{_page + 1} / {_pages.Length}</color>");
                _pageIndicatorText.text = sb.ToString();
            }

            if (_selectHint != null && page.selectPrompt != null) _selectHint.SetData(page.selectPrompt);

            for (int i = 0; i < _rows.Length; i++)
            {
                var row = _rows[i];
                if (row == null) continue;
                bool exists = i < n;
                if (row.button != null) row.button.gameObject.SetActive(exists);
                if (!exists) continue;
                bool on = i == sel;
                if (row.band != null) row.band.enabled = on;
                if (row.bar != null) row.bar.enabled = on;
                if (row.marker != null) row.marker.enabled = on;
                if (row.label != null)
                {
                    row.label.text = page.entries[i]?.shortName ?? string.Empty;
                    row.label.color = on ? _rowTextOn : _rowTextOff;
                }
            }

            ApplyArt(page, entry);
            if (_nameText != null) _nameText.text = entry?.charName ?? string.Empty;
            if (_subtitleText != null) _subtitleText.text = entry?.subtitle ?? string.Empty;
            if (_descriptionText != null) _descriptionText.text = entry?.description ?? string.Empty;
        }

        /// <summary>依立繪比例縮放到「最大高 × 最大寬」以內，腳底中心對齊基準點。</summary>
        private void ApplyArt(CodexPage page, CharEntry entry)
        {
            if (_artImage == null) return;
            var sprite = entry?.art;
            _artImage.sprite = sprite;
            _artImage.enabled = sprite != null;
            if (sprite == null) return;

            Vector2 s = sprite.rect.size;
            float aspect = s.x / Mathf.Max(1f, s.y);
            float h = page.artMaxHeight, w = h * aspect;
            if (w > page.artMaxWidth) { w = page.artMaxWidth; h = w / aspect; }
            float scale = entry.artScale > 0f ? entry.artScale : 1f;

            var rt = _artImage.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(w * scale, h * scale);
            rt.anchoredPosition = page.artFootPosition + new Vector2(entry.artOffsetX, 0f);
        }
    }
}
