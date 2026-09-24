using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

namespace PilgrimOfSin
{
    /// <summary>
    /// 「製作團隊、官方社群」頁面的翻頁書邏輯。
    /// 文字內容統一從 CreditsData 讀取（與結局跑馬燈 EndingCreditsRoll 共用同一份資料），
    /// 這裡只負責切換顯示哪一頁。
    /// </summary>
    public class CreditsPageBook : MonoBehaviour
    {
        [Header("資料來源")]
        [SerializeField] private CreditsData _creditsData;

        [Header("翻頁按鈕")]
        [SerializeField] private Button _leftArrowBtn;
        [SerializeField] private Button _rightArrowBtn;

        [Header("內容顯示")]
        [SerializeField] private TextMeshProUGUI _pageText;

        private int _currentPage = 0;

        // 左搖桿左右屬於類比輸入，需要自行做邊緣偵測（超過閾值那一瞬間才觸發一次），
        // 避免搖桿持續推著不放時每幀都翻頁。
        private const float StickThreshold = 0.5f;
        private bool _stickWasLeft;
        private bool _stickWasRight;

        private void OnEnable()
        {
            _currentPage = 0;
            _stickWasLeft = false;
            _stickWasRight = false;
            RefreshDisplay();
        }

        private void Start()
        {
            _leftArrowBtn.onClick.AddListener(PrevPage);
            _rightArrowBtn.onClick.AddListener(NextPage);
        }

        private void Update()
        {
            // 這個頁面同時用在主選單（無 PlayerInputReader 可用）跟 ESC 選單，
            // 所以比照 MainMenuUI 的作法直接讀裝置，不依賴 PlayerInputReader。
            if (Keyboard.current != null)
            {
                if (Keyboard.current.leftArrowKey.wasPressedThisFrame)
                    PrevPage();
                else if (Keyboard.current.rightArrowKey.wasPressedThisFrame)
                    NextPage();
            }

            if (Gamepad.current == null) return;

            if (Gamepad.current.leftShoulder.wasPressedThisFrame || Gamepad.current.dpad.left.wasPressedThisFrame)
                PrevPage();
            else if (Gamepad.current.rightShoulder.wasPressedThisFrame || Gamepad.current.dpad.right.wasPressedThisFrame)
                NextPage();

            float stickX = Gamepad.current.leftStick.ReadValue().x;
            bool stickLeft = stickX < -StickThreshold;
            bool stickRight = stickX > StickThreshold;

            if (stickLeft && !_stickWasLeft)
                PrevPage();
            else if (stickRight && !_stickWasRight)
                NextPage();

            _stickWasLeft = stickLeft;
            _stickWasRight = stickRight;
        }

        private void PrevPage()
        {
            if (_currentPage <= 0) return;
            _currentPage--;
            RefreshDisplay();
        }

        private void NextPage()
        {
            if (_creditsData == null || _currentPage >= _creditsData.PageCount - 1) return;
            _currentPage++;
            RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            if (_creditsData == null || _creditsData.PageCount == 0) return;

            _currentPage = Mathf.Clamp(_currentPage, 0, _creditsData.PageCount - 1);

            if (_pageText != null)
                _pageText.text = _creditsData.GetPage(_currentPage);

            if (_leftArrowBtn != null)
                _leftArrowBtn.interactable = _currentPage > 0;
            if (_rightArrowBtn != null)
                _rightArrowBtn.interactable = _currentPage < _creditsData.PageCount - 1;
        }
    }
}
