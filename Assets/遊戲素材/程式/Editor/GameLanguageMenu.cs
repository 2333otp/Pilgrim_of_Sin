#if UNITY_EDITOR
using UnityEditor;

namespace PilgrimOfSin.EditorTools
{
    /// <summary>測試用：從選單切換目前配音語言（等同呼叫 GameLanguage.Current），打勾的是目前語言。</summary>
    public static class GameLanguageMenu
    {
        private const string Root = "PilgrimOfSin/配音語言/";

        [MenuItem(Root + "台語")]
        private static void SetTaiwanese() => GameLanguage.Current = VoiceLanguage.Taiwanese;

        [MenuItem(Root + "台語", true)]
        private static bool ValidateTaiwanese()
        {
            Menu.SetChecked(Root + "台語", GameLanguage.Current == VoiceLanguage.Taiwanese);
            return true;
        }

        [MenuItem(Root + "英文")]
        private static void SetEnglish() => GameLanguage.Current = VoiceLanguage.English;

        [MenuItem(Root + "英文", true)]
        private static bool ValidateEnglish()
        {
            Menu.SetChecked(Root + "英文", GameLanguage.Current == VoiceLanguage.English);
            return true;
        }
    }
}
#endif
