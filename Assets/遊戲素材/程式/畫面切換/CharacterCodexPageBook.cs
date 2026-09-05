using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace PilgrimOfSin
{
    /// <summary>
    /// 「角色資料、心魔圖鑑」翻頁書。玩家狀態面板的分頁內容之一，由 PauseMenuUI 開關。
    /// 每頁一張直式立繪 + 右側名稱與介紹文字。
    /// 翻頁邏輯比照 MemoryPageBook / WeaponInfoPageBook（左右箭頭 + 手把 L1/R1，到頭停、不循環）。
    /// 順序固定：繆爾（角色）→ 阿貪 → 阿嗔 → 阿痴（心魔照貪嗔痴關卡順序）。
    /// </summary>
    public class CharacterCodexPageBook : MonoBehaviour
    {
        [System.Serializable]
        public class CharPage
        {
            public Sprite art;
            public string charName;
            [TextArea(2, 5)] public string description;
            [Tooltip("這頁立繪方框的高度（會依 Preserve Aspect 縮放）。繆爾放大、心魔小一點。")]
            public float artHeight = 1000f;
            [Tooltip("這頁立繪的垂直位置（X 沿用 Inspector 設定）")]
            public float artPosY = -20f;
        }

        [Header("翻頁按鈕")]
        [SerializeField] private Button _leftArrowBtn;
        [SerializeField] private Button _rightArrowBtn;

        [Header("內容顯示")]
        [SerializeField] private Image _artImage;
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private TextMeshProUGUI _descriptionText;
        [SerializeField] private TextMeshProUGUI _pageIndicatorText;

        [Header("頁面資料（順序：繆爾 / 阿貪 / 阿嗔 / 阿痴）")]
        [SerializeField] private CharPage[] _pages = new CharPage[4];

        private int _currentPage = 0;

        private void OnEnable()
        {
            _currentPage = 0;
            RefreshDisplay();
        }

        private void Start()
        {
            _leftArrowBtn.onClick.AddListener(PrevPage);
            _rightArrowBtn.onClick.AddListener(NextPage);
        }

        private void Update()
        {
            // 這頁只在 PauseMenuUI 的玩家狀態分頁底下才會顯示，InputReader 一定拿得到。
            var reader = PauseMenuUI.Instance != null ? PauseMenuUI.Instance.InputReader : null;
            if (reader == null) return;

            if (reader.MenuPageLeftPressed)
                PrevPage();
            else if (reader.MenuPageRightPressed)
                NextPage();
        }

        private void PrevPage()
        {
            if (_currentPage <= 0) return;
            _currentPage--;
            RefreshDisplay();
        }

        private void NextPage()
        {
            if (_currentPage >= _pages.Length - 1) return;
            _currentPage++;
            RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            if (_pages == null || _pages.Length == 0) return;

            _currentPage = Mathf.Clamp(_currentPage, 0, _pages.Length - 1);
            var page = _pages[_currentPage];

            if (_artImage != null)
            {
                _artImage.sprite  = page?.art;
                _artImage.enabled = page?.art != null;

                var rt = _artImage.rectTransform;
                float h = (page != null && page.artHeight > 0f) ? page.artHeight : 1000f;
                rt.sizeDelta = new Vector2(h, h);
                if (page != null)
                    rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, page.artPosY);
            }

            if (_nameText != null)
                _nameText.text = page?.charName ?? string.Empty;

            if (_descriptionText != null)
                _descriptionText.text = page?.description ?? string.Empty;

            if (_pageIndicatorText != null)
                _pageIndicatorText.text = $"{_currentPage + 1} / {_pages.Length}";

            if (_leftArrowBtn != null)
                _leftArrowBtn.interactable = _currentPage > 0;
            if (_rightArrowBtn != null)
                _rightArrowBtn.interactable = _currentPage < _pages.Length - 1;
        }
    }
}
