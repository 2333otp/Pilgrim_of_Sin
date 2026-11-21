using System;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// Boss 一個動作的全部素材：動畫、音效、配音、特效，攻擊動作另外有命中判定設定。
    /// 格式跟主角的 PlayerActionEntry 一致（特效直接沿用 PlayerVfxEntry），三隻 Boss 共用。
    /// 對應 Boss Animator Controller 裡一個 state（state 名稱 = stateName）。
    /// </summary>
    [Serializable]
    public class BossActionEntry
    {
        [Tooltip("Animator Controller 裡的 state 名稱，程式用它對應這筆資料。一般不要改。")]
        public string stateName;

        [Header("動畫")]
        [Tooltip("這個動作要播的動畫。換成別的 Clip，動作時間會自動跟著 Clip 長度走。留空 = 維持 Animator 原本的動畫。")]
        public AnimationClip clip;

        [Tooltip("Animator Controller 這個 state 原本綁的 Clip（自動填入，請勿修改）。Clip 欄位跟它不同時，執行時會把它換掉。")]
        public AnimationClip baseClip;

        [Min(0f)]
        [Tooltip("動作時間 = Clip 長度 + 這個餘裕（秒）。")]
        public float endPadding;

        [Min(0f)]
        [Tooltip("保險時間（秒）：沒有 Clip 可以量長度時，動作持續這麼久。0 = 用程式內建值。")]
        public float fallbackDuration;

        [Header("音效")]
        [Tooltip("音效音檔，直接把 wav / ogg / mp3 拖進來。可任意增減，每次隨機播放其中一個。留空 = 不播。")]
        public AudioClip[] sfx = new AudioClip[0];

        [Range(0f, 1f)] [Tooltip("音效音量。")]
        public float sfxVolume = 1f;

        [Range(0f, 1f)]
        [Tooltip("音效播放時間點：動作開始後經過動畫長度的比例。0 = 一開始就播，0.3 = 動畫播到 30% 才播。")]
        public float sfxNormalizedTime;

        [Header("配音")]
        [Tooltip("配音（英文版），可放多個，每次隨機播放其中一個。語言設為英文時才會播。留空 = 不播。")]
        public AudioClip[] voiceEnglish = new AudioClip[0];

        [Tooltip("配音（台語版），可放多個，每次隨機播放其中一個。語言設為台語時才會播。留空 = 不播。")]
        public AudioClip[] voiceTaiwanese = new AudioClip[0];

        [Range(0f, 1f)] [Tooltip("配音音量（英文、台語共用）。")]
        public float voiceVolume = 1f;

        [Range(0f, 1f)] [Tooltip("配音播放時間點，用法同音效。")]
        public float voiceNormalizedTime;

        [Header("特效")]
        [Tooltip("特效清單，可多個；Prefab 留空 = 不生成。骨骼名稱請填 Boss 模型裡的骨頭名稱。")]
        public PlayerVfxEntry[] vfx = new PlayerVfxEntry[0];

        [Header("攻擊判定（只有攻擊動作會用到，其他動作不用管）")]
        [Min(0f)] [Tooltip("命中時對主角造成的傷害。")]
        public float damage;

        [Min(0f)] [Tooltip("Boss 離主角多近時會選這招（AI 決策用）。")]
        public float attackRange;

        [Range(0f, 1f)]
        [Tooltip("命中判定時間點：動畫播到這個比例的那一刻判定一次（例如揮下去的那一幀）。")]
        public float hitNormalizedTime = 0.5f;

        [Min(0f)] [Tooltip("命中判定距離（公尺）：主角在 Boss 這個距離內才會被打到。")]
        public float hitRadius;

        [Range(0f, 360f)]
        [Tooltip("命中判定角度：以 Boss 正前方為中心的扇形角度。360 = 全方位（例如震地攻擊）。")]
        public float hitAngle = 120f;

        public BossActionEntry() { }
        public BossActionEntry(string stateName) { this.stateName = stateName; }

        /// <summary>依語言取得配音清單（只取該語言的，不會跨語言替補）。</summary>
        public AudioClip[] GetVoice(VoiceLanguage language)
            => language == VoiceLanguage.English ? voiceEnglish : voiceTaiwanese;

        /// <summary>這筆動作實際要播的 Clip（有換就用換的，沒換就用 Animator 原本的）。</summary>
        public AnimationClip EffectiveClip => clip != null ? clip : baseClip;

        /// <summary>動作時間：有 Clip 時 = Clip 長度 + 餘裕；沒有 Clip 時用保險時間，再沒有就用 defaultValue。</summary>
        public float GetDuration(float defaultValue)
        {
            var c = EffectiveClip;
            if (c != null) return c.length + endPadding;
            return fallbackDuration > 0f ? fallbackDuration : defaultValue;
        }
    }
}
