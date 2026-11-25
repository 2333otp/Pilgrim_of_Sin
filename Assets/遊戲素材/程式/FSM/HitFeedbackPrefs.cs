using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 玩家的打擊感偏好（設置選單「畫面與震動」）：鏡頭晃動強度、手把震動開關。
    /// 存在 PlayerPrefs（跟語言設定一樣），不放進 save.json：
    /// save.json 代表「有沒有遊戲進度」，在主選單調設定不應該因此產生存檔。
    /// </summary>
    public static class HitFeedbackPrefs
    {
        private const string ShakeKey = "CameraShakeIntensity";
        private const string RumbleKey = "ControllerRumble";

        /// <summary>預設 70%：動暈考量，一開始就比設計值收斂一點，想要更強的玩家自己調高。</summary>
        public const float DefaultShakeIntensity = 0.7f;

        /// <summary>鏡頭晃動強度 0~1（0 = 完全不晃）。</summary>
        public static float ShakeIntensity
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat(ShakeKey, DefaultShakeIntensity));
            set => PlayerPrefs.SetFloat(ShakeKey, Mathf.Clamp01(value));
        }

        /// <summary>手把震動（預設開）。</summary>
        public static bool RumbleEnabled
        {
            get => PlayerPrefs.GetInt(RumbleKey, 1) != 0;
            set => PlayerPrefs.SetInt(RumbleKey, value ? 1 : 0);
        }

        /// <summary>寫入磁碟（離開設定頁時呼叫一次就好，不用每次拖滑桿都寫）。</summary>
        public static void Save() => PlayerPrefs.Save();
    }
}
