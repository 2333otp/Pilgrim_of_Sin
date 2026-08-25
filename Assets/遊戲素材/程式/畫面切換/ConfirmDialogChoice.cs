using TMPro;
using UnityEngine;

namespace PilgrimOfSin
{
    /// <summary>
    /// 掛在確認對話框的「是／返回遊戲」按鈕上，選中時顯示反白列，
    /// 並在文字前面加上「&gt;」符號。文字與符號合併成同一個 TMP 物件、
    /// 一起置中，避免箭頭跟文字分開兩個物件各自定位、容易對不齊。
    /// 由 PauseMenuUI 的 ApplyButtonColor 在滑鼠 hover／手把左右導航切換選取時呼叫。
    /// </summary>
    public class ConfirmDialogChoice : MonoBehaviour
    {
        [SerializeField] private GameObject _highlightGroup;
        [SerializeField] private TextMeshProUGUI _label;

        private string _baseText;

        private void Awake()
        {
            if (_label != null) _baseText = _label.text;
        }

        public void SetSelected(bool selected)
        {
            if (_highlightGroup != null) _highlightGroup.SetActive(selected);
            if (_label != null)
            {
                if (_baseText == null) _baseText = _label.text;
                _label.text = selected ? "> " + _baseText : _baseText;
            }
        }
    }
}
