using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PilgrimOfSin
{
    /// <summary>
    /// 共用確認對話框（例如「尚未保存進度，確定返回主選單？」）。
    /// 邊框/底圖沿用「提示框、音量底圖.png」，文字與按鈕拼組而成，非整圖烤死。
    /// 訊息文字由 PauseMenuUI 依用途（返回主選單／返回小木屋）透過 SetMessage 設定。
    /// </summary>
    public class ConfirmDialogPanel : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _messageText;
        [SerializeField] private Button _btnConfirm; // 是
        [SerializeField] private Button _btnCancel;  // 返回遊戲

        public Button BtnConfirm => _btnConfirm;
        public Button BtnCancel => _btnCancel;

        public void SetMessage(string text)
        {
            if (_messageText != null) _messageText.text = text;
        }
    }
}
