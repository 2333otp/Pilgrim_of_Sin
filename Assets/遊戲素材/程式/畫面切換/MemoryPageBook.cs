using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace PilgrimOfSin
{
    /// <summary>
    /// 「過往記憶」CG 翻頁書。玩家狀態面板的分頁內容之一，由 PauseMenuUI 開關。
    /// 每頁一張 CG + 一段敘事文字，翻頁邏輯比照 CreditsPageBook（左右鍵按鈕 + 手把 L1/R1）。
    /// </summary>
    public class MemoryPageBook : MonoBehaviour
    {
        [System.Serializable]
        public class MemoryPage
        {
            public Sprite cgImage;
            [TextArea(2, 5)] public string captionText;
        }

        [Header("翻頁按鈕")]
        [SerializeField] private Button _leftArrowBtn;
        [SerializeField] private Button _rightArrowBtn;

        [Header("內容顯示")]
        [SerializeField] private Image _cgImage;
        [SerializeField] private TextMeshProUGUI _captionText;
        [SerializeField] private TextMeshProUGUI _pageIndicatorText;

        [Header("記憶頁面資料")]
        [SerializeField] private MemoryPage[] _pages = new MemoryPage[17];

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
            // 不用 Gamepad.current.leftShoulder.wasPressedThisFrame 直接輪詢——
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

            if (_cgImage != null)
            {
                _cgImage.sprite  = page?.cgImage;
                _cgImage.enabled = page?.cgImage != null;
            }

            if (_captionText != null)
                _captionText.text = page?.captionText ?? string.Empty;

            if (_pageIndicatorText != null)
                _pageIndicatorText.text = $"{_currentPage + 1} / {_pages.Length}";

            if (_leftArrowBtn != null)
                _leftArrowBtn.interactable = _currentPage > 0;
            if (_rightArrowBtn != null)
                _rightArrowBtn.interactable = _currentPage < _pages.Length - 1;
        }
    }
}
