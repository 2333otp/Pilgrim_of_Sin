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
            if (Gamepad.current == null) return;

            if (Gamepad.current.leftShoulder.wasPressedThisFrame)
                PrevPage();
            else if (Gamepad.current.rightShoulder.wasPressedThisFrame)
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
