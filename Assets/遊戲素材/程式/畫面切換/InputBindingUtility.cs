using UnityEngine.InputSystem;

namespace PilgrimOfSin
{
    /// <summary>
    /// 共用的 Input System 查詢輔助方法：從 PlayerInput 判斷目前裝置 Scheme、
    /// 從 Action 找出該 Scheme 下實際綁定的 Control Path。
    /// 供 InputPromptUI、GlyphHighlightIcon 共用，避免重複實作。
    /// </summary>
    public static class InputBindingUtility
    {
        /// <summary>取得目前使用中的 ControlScheme 名稱，偵測不到時預設回傳 "Gamepad"。</summary>
        public static string GetCurrentScheme(PlayerInput playerInput)
        {
            return playerInput != null && !string.IsNullOrEmpty(playerInput.currentControlScheme)
                ? playerInput.currentControlScheme
                : "Gamepad";
        }

        /// <summary>在指定 Action 裡找出屬於某個 ControlScheme group 的第一條非 Composite binding path。</summary>
        public static string FindBindingPath(InputAction action, string schemeGroup)
        {
            if (action == null || string.IsNullOrEmpty(schemeGroup)) return null;

            foreach (var binding in action.bindings)
            {
                if (binding.isComposite || string.IsNullOrEmpty(binding.effectivePath)) continue;
                if (string.IsNullOrEmpty(binding.groups)) continue;

                foreach (var group in binding.groups.Split(InputBinding.Separator))
                {
                    if (group == schemeGroup)
                        return binding.effectivePath;
                }
            }
            return null;
        }
    }
}
