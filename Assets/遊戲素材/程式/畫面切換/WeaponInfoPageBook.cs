using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace PilgrimOfSin
{
    /// <summary>
    /// 「武器介紹」翻頁書。玩家狀態面板的分頁內容之一，由 PauseMenuUI 開關。
    /// 每頁一把武器：左側去背原畫 + 右側武器名與介紹文字。
    /// 翻頁邏輯比照 MemoryPageBook（左右箭頭按鈕 + 手把 L1/R1，到頭停、不循環）。
    /// 頁面順序固定跟各 Boss 場景下方武器欄由左到右一致：鉛筆 / 畫筆 / 調色刀 / 調色盤。
    /// </summary>
    public class WeaponInfoPageBook : MonoBehaviour
    {
        [System.Serializable]
        public class WeaponPage
        {
            public Sprite art;
            public string weaponName;
            [TextArea(2, 5)] public string description;
            [Tooltip("勾選：左側原畫以兩把交叉呈現（比照武器欄的調色刀圖示）。取消：單張。")]
            public bool crossed = true;
            [Tooltip("非交叉時單張原畫的旋轉角度（度），比照武器欄圖示的斜放")]
            public float singleRotation = 0f;
        }

        [Header("翻頁按鈕")]
        [SerializeField] private Button _leftArrowBtn;
        [SerializeField] private Button _rightArrowBtn;

        [Header("內容顯示")]
        [Tooltip("主原畫：交叉時為其中一把，非交叉時為單張")]
        [SerializeField] private Image _artImage;
        [Tooltip("交叉用的第二把原畫，非交叉頁面自動隱藏")]
        [SerializeField] private Image _artImageB;
        [Tooltip("交叉時每把傾斜角度（度）")]
        [SerializeField] private float _crossAngle = 23f;
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private TextMeshProUGUI _descriptionText;
        [SerializeField] private TextMeshProUGUI _pageIndicatorText;

        [Header("武器頁面資料（順序：鉛筆/畫筆/調色刀/調色盤）")]
        [SerializeField] private WeaponPage[] _pages = new WeaponPage[4];

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
            // 走跟 ESC 選單其他手把輸入一樣的 PlayerInputReader 事件旗標，
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

            bool cross = page != null && page.crossed;
            bool hasArt = page?.art != null;
            float singleRot = page != null ? page.singleRotation : 0f;

            if (_artImage != null)
            {
                _artImage.sprite  = page?.art;
                _artImage.enabled = hasArt;
                _artImage.rectTransform.localRotation = Quaternion.Euler(0f, 0f, cross ? _crossAngle : singleRot);
            }

            if (_artImageB != null)
            {
                _artImageB.sprite = page?.art;
                _artImageB.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -_crossAngle);
                _artImageB.gameObject.SetActive(cross && hasArt);
            }

            if (_nameText != null)
                _nameText.text = page?.weaponName ?? string.Empty;

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
