using UnityEngine;
using UnityEngine.InputSystem;

namespace PilgrimOfSin
{
    /// <summary>
    /// 單一操作提示的資料：提示文字 + 要顯示哪個 Input Action 的按鍵圖示。
    /// 圖示本身不存在這裡，而是由 InputPromptUI 在執行期依目前裝置(手把/鍵盤)
    /// 查詢這個 Action 實際綁定的按鍵，再到 InputGlyphDatabase 換成對應 Sprite。
    /// </summary>
    [CreateAssetMenu(fileName = "InputPromptData", menuName = "Pilgrim of Sin/UI/操作提示資料 (InputPromptData)")]
    public class InputPromptData : ScriptableObject
    {
        [Tooltip("提示文字，例如「撿取錢袋」「攻擊」")]
        [SerializeField] private string _promptText;

        [Tooltip("這個提示對應 PlayerInputActions 裡的哪個 Action，按鍵圖示會依此 Action 目前的實際綁定自動決定")]
        [SerializeField] private InputActionReference _action;

        public string PromptText => _promptText;
        public InputActionReference Action => _action;
    }
}
