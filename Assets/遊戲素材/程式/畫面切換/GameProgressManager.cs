using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Audio;

namespace PilgrimOfSin
{
    /// <summary>
    /// 遊戲進度管理器（DontDestroyOnLoad 單例）。
    /// 統一管理：
    ///   - 存檔讀寫（JSON）
    ///   - Boss 通關記錄
    ///   - 音量設定（需在 Inspector 指定 AudioMixer）
    /// </summary>
    public class GameProgressManager : MonoBehaviour
    {
        public static GameProgressManager Instance { get; private set; }

        [Header("AudioMixer（需在 Inspector 指定）")]
        [SerializeField] private AudioMixer _audioMixer;

        private SaveData _data = new SaveData();
        private string SavePath => Path.Combine(Application.persistentDataPath, "save.json");

        // ── 生命週期 ─────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Load();
        }

        // ── Boss 通關記錄 ────────────────────────────────────────────

        public bool IsBossDefeated(SceneTransitionManager.BossType boss)
            => _data.defeatedBosses.Contains(boss.ToString());

        public void MarkBossDefeated(SceneTransitionManager.BossType boss)
        {
            string key = boss.ToString();
            if (!_data.defeatedBosses.Contains(key))
                _data.defeatedBosses.Add(key);
        }

        /// <summary>回傳「已成功克服Ｘ、Ｙ之心魔」格式的文字，供玩家狀態頁顯示。</summary>
        public string GetDefeatedBossesText()
        {
            if (_data.defeatedBosses.Count == 0)
                return "尚未克服任何心魔";

            var names = new List<string>();
            foreach (string b in _data.defeatedBosses)
            {
                switch (b)
                {
                    case "Greed":   names.Add("貪"); break;
                    case "Wrath":   names.Add("嗔"); break;
                    case "Foolish": names.Add("癡"); break;
                }
            }
            return $"已成功克服{string.Join("、", names)}之心魔";
        }

        // ── 音量存取 ─────────────────────────────────────────────────

        public float MasterVolume => _data.masterVolume;
        public float MusicVolume  => _data.musicVolume;
        public float SFXVolume    => _data.sfxVolume;

        public void SetMasterVolume(float value)
        {
            _data.masterVolume = value;
            ApplyMixerVolume("MasterVolume", value);
        }

        public void SetMusicVolume(float value)
        {
            _data.musicVolume = value;
            ApplyMixerVolume("MusicVolume", value);
        }

        public void SetSFXVolume(float value)
        {
            _data.sfxVolume = value;
            ApplyMixerVolume("SFXVolume", value);
        }

        public void ApplyAllVolumes()
        {
            ApplyMixerVolume("MasterVolume", _data.masterVolume);
            ApplyMixerVolume("MusicVolume",  _data.musicVolume);
            ApplyMixerVolume("SFXVolume",    _data.sfxVolume);
        }

        // Slider 值 0~1 轉換為 AudioMixer dB（-80 dB ~ 0 dB）
        private void ApplyMixerVolume(string exposedParam, float linearValue)
        {
            if (_audioMixer == null) return;
            float db = linearValue > 0.0001f ? Mathf.Log10(linearValue) * 20f : -80f;
            _audioMixer.SetFloat(exposedParam, db);
        }

        // ── 存檔 / 讀檔 ─────────────────────────────────────────────

        public void Save()
        {
            File.WriteAllText(SavePath, JsonUtility.ToJson(_data, true));
        }

        private void Load()
        {
            if (!File.Exists(SavePath)) return;
            try
            {
                _data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
                ApplyAllVolumes();
            }
            catch
            {
                _data = new SaveData();
                Debug.LogWarning("[GameProgress] 存檔讀取失敗，使用預設值");
            }
        }
    }
}
