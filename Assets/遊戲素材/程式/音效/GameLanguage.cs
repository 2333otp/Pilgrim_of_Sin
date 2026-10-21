using System;
using UnityEngine;

namespace PilgrimOfSin
{
    /// <summary>配音語言。要加新語言就在這裡加一項，並在 PlayerActionEntry 補對應的配音清單。</summary>
    public enum VoiceLanguage
    {
        Taiwanese = 0,
        English = 1,
    }

    /// <summary>
    /// 全域語言設定。用 PlayerPrefs 記住，下次開遊戲沿用；目前只影響配音（英文／台語）。
    /// 之後做設置選單的語言選項時，直接設 GameLanguage.Current 即可，其他地方會自動跟著變。
    /// </summary>
    public static class GameLanguage
    {
        private const string PrefsKey = "GameLanguage";
        private const VoiceLanguage DefaultLanguage = VoiceLanguage.Taiwanese;

        private static VoiceLanguage? _current;

        /// <summary>語言被切換時觸發（參數是新語言）。</summary>
        public static event Action<VoiceLanguage> Changed;

        public static VoiceLanguage Current
        {
            get
            {
                if (_current == null)
                {
                    int saved = PlayerPrefs.GetInt(PrefsKey, (int)DefaultLanguage);
                    _current = Enum.IsDefined(typeof(VoiceLanguage), saved) ? (VoiceLanguage)saved : DefaultLanguage;
                }
                return _current.Value;
            }
            set
            {
                if (_current == value) return;
                _current = value;
                PlayerPrefs.SetInt(PrefsKey, (int)value);
                PlayerPrefs.Save();
                Changed?.Invoke(value);
            }
        }

        // 關閉 Domain Reload 進 Play Mode 時，靜態變數不會自動清掉，要手動重置。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _current = null;
            Changed = null;
        }
    }
}
