using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace PilgrimOfSin
{
    /// <summary>
    /// 「製作團隊、官方社群」頁面的翻頁書本邏輯。
    /// 每頁是獨立的文字物件（直接在該物件的 TextMeshPro 欄位打字即可），
    /// 這裡只負責切換哪一頁的物件是啟用狀態，不會覆寫任何人打的文字。
    /// </summary>
    public class CreditsPageBook : MonoBehaviour
    {
        [Header("翻頁按鈕")]
        [SerializeField] private Button _leftArrowBtn;
        [SerializeField] private Button _rightArrowBtn;

        [Header("每頁的文字物件（依序：第1頁～第4頁）")]
        [SerializeField] private GameObject[] _pageObjects = new GameObject[4];

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
            if (_currentPage >= _pageObjects.Length - 1) return;
            _currentPage++;
            RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            if (_pageObjects == null || _pageObjects.Length == 0)
                return;

            _currentPage = Mathf.Clamp(_currentPage, 0, _pageObjects.Length - 1);

            for (int i = 0; i < _pageObjects.Length; i++)
            {
                if (_pageObjects[i] != null)
                    _pageObjects[i].SetActive(i == _currentPage);
            }

            if (_leftArrowBtn != null)
                _leftArrowBtn.interactable = _currentPage > 0;
            if (_rightArrowBtn != null)
                _rightArrowBtn.interactable = _currentPage < _pageObjects.Length - 1;
        }
    }
}
