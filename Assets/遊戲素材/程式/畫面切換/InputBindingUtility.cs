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
        /// <summary>
        /// 取得目前使用中的 ControlScheme 名稱，偵測不到時預設回傳 "Gamepad"。
        ///
        /// 【手把插著但還沒被摸過時，優先顯示手把圖示】
        /// 鍵盤/滑鼠在 PlayerInputReader.OnEnable() 就會主動配對，手把則刻意保持未配對
        /// （見 PlayerInputReader.cs 註解），玩家實際按下手把前，PlayerInput.currentControlScheme
        /// 會一路停在 "Keyboard&Mouse"——這只是配對順序的技術結果，不代表玩家真的在用鍵盤。
        /// 鍵盤的按鍵圖示素材目前也還沒補齊，所以只要偵測到有手把裝置存在，一律優先顯示
        /// 手把圖示，避免玩家一進場景看到的操作提示是空白的。
        /// </summary>
        public static string GetCurrentScheme(PlayerInput playerInput)
        {
            if (Gamepad.current != null) return "Gamepad";

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
