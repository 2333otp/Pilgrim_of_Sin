using System;
using UnityEngine;
using UnityEngine.Audio;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>癡的配音種類。</summary>
    public enum FoolishVoiceId
    {
        Roar,       // 嘶吼：惡區即將重新出現的信號
        Attack1,
        Attack2,
        Attack3,
        Hurt,       // 受擊（有最小間隔，避免連續受傷時一直喊）
        Dead,
    }

    /// <summary>一句配音：英文、台語各一份清單，每次隨機播其中一個。清單留空 = 不播。</summary>
    [Serializable]
    public class FoolishVoiceLine
    {
        [Tooltip("配音（英文版）音檔，可放多個，每次隨機播放其中一個。語言設為英文時才會播。留空 = 不播。")]
        public AudioClip[] voiceEnglish = new AudioClip[0];

        [Tooltip("配音（台語版）音檔，可放多個，每次隨機播放其中一個。語言設為台語時才會播。留空 = 不播。")]
        public AudioClip[] voiceTaiwanese = new AudioClip[0];

        [Range(0f, 1f)]
        [Tooltip("配音音量（英文、台語共用）。")]
        public float volume = 1f;

        [Min(0f)]
        [Tooltip("延遲幾秒才播（例如等前搖動作到一半）。0 = 立刻播。")]
        public float delay;

        /// <summary>依語言取得配音清單（只取該語言的，不會跨語言替補）。</summary>
        public AudioClip[] GetVoice(VoiceLanguage language)
            => language == VoiceLanguage.English ? voiceEnglish : voiceTaiwanese;
    }

    /// <summary>
    /// 癡的配音。沿用玩家同一套 GameLanguage：玩家設定的配音語言會自動套用到癡，
    /// 中途切換語言也會在下一次播放時生效。
    /// 掛在 FoolishBoss 上；沒掛這個元件時，癡的行為與沒有配音系統時完全一樣。
    /// </summary>
    [DisallowMultipleComponent]
    public class FoolishVoice : MonoBehaviour
    {
        [Header("音訊輸出")]
        [Tooltip("配音要輸出到哪個 Mixer 群組（建議與玩家配音相同，會跟著遊戲設定裡的音效音量走）。")]
        [SerializeField] private AudioMixerGroup _mixerGroup;

        [Header("配音")]
        [SerializeField] private FoolishVoiceLine _roar = new FoolishVoiceLine();
        [SerializeField] private FoolishVoiceLine _attack1 = new FoolishVoiceLine();
        [SerializeField] private FoolishVoiceLine _attack2 = new FoolishVoiceLine();
        [SerializeField] private FoolishVoiceLine _attack3 = new FoolishVoiceLine();
        [SerializeField] private FoolishVoiceLine _hurt = new FoolishVoiceLine();
        [SerializeField] private FoolishVoiceLine _dead = new FoolishVoiceLine();

        [Header("受擊配音")]
        [Min(0f)]
        [Tooltip("受擊配音的最小間隔（秒）。連續被打時避免每一下都喊。")]
        [SerializeField] private float _hurtMinInterval = 1.5f;

        private float _nextHurtTime;
        private AudioClip _lastClip;

        /// <summary>播放指定配音。對應的清單為空就什麼都不做。</summary>
        public void Play(FoolishVoiceId id)
        {
            if (id == FoolishVoiceId.Hurt)
            {
                if (Time.time < _nextHurtTime) return;
                _nextHurtTime = Time.time + _hurtMinInterval;
            }

            var line = GetLine(id);
            if (line == null) return;
            PlayRandom(line.GetVoice(GameLanguage.Current), line.volume, line.delay);
        }

        /// <summary>依攻擊狀態播放對應的攻擊配音。</summary>
        public void PlayAttack(FoolishBossStateType attackType)
        {
            switch (attackType)
            {
                case FoolishBossStateType.Attack1: Play(FoolishVoiceId.Attack1); break;
                case FoolishBossStateType.Attack2: Play(FoolishVoiceId.Attack2); break;
                case FoolishBossStateType.Attack3: Play(FoolishVoiceId.Attack3); break;
            }
        }

        private FoolishVoiceLine GetLine(FoolishVoiceId id) => id switch
        {
            FoolishVoiceId.Roar => _roar,
            FoolishVoiceId.Attack1 => _attack1,
            FoolishVoiceId.Attack2 => _attack2,
            FoolishVoiceId.Attack3 => _attack3,
            FoolishVoiceId.Hurt => _hurt,
            FoolishVoiceId.Dead => _dead,
            _ => null,
        };

        /// <summary>從清單隨機播一個音檔（跳過空格；有 2 個以上時避免連續兩次播到同一個）。</summary>
        private void PlayRandom(AudioClip[] list, float volume, float delay)
        {
            if (list == null || list.Length == 0) return;

            int count = 0;
            foreach (var c in list) if (c != null) count++;
            if (count == 0) return;

            AudioClip chosen = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                int pick = UnityEngine.Random.Range(0, count);
                foreach (var c in list)
                {
                    if (c == null) continue;
                    if (pick-- != 0) continue;
                    chosen = c;
                    break;
                }
                if (count == 1 || chosen != _lastClip) break;
            }

            _lastClip = chosen;
            SfxPlayer.PlayClip(chosen, volume, 1f, _mixerGroup, 0f, transform.position, delay);
        }
    }
}
