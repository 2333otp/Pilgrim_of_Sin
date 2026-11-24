using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 天秤（Libra）設定，企劃可直接在 Assets/遊戲素材/SO/Boss 底下調整，不用開場景。
    /// 包含：受擊觸發門檻、每個動畫 state 的動畫 / 音效 / 配音 / 特效、被攻擊時的音效 / 特效。
    /// </summary>
    [CreateAssetMenu(fileName = "GreedScaleConfig", menuName = "PilgrimOfSin/Greed Scale Config")]
    public class GreedScaleConfig : ScriptableObject
    {
        [Tooltip("玩家累積攻擊天秤幾下才觸發一次「受擊+重製」動畫（打落錢袋、重新生成一批）。")]
        [Min(1)]
        [SerializeField] private int _hitsRequiredToTrigger = 1;

        public int HitsRequiredToTrigger => _hitsRequiredToTrigger;

        [Header("動畫")]
        [Tooltip("天秤的 Animator Controller（LibraScale.controller）。Inspector 最下面的按鈕會從這裡讀出所有 state。")]
        public RuntimeAnimatorController animatorController;

        [Tooltip("每個動畫 state 一筆：可換動畫，並設定這個 state 開始播時的音效 / 配音 / 特效。\n" +
                 "動畫轉換規則（什麼時候切到哪個 state）仍在 Animator Controller 裡，這裡只換「播什麼」。")]
        public List<ScaleAnimEntry> states = new List<ScaleAnimEntry>();

        [Header("被攻擊（玩家每一擊打到天秤）")]
        [Tooltip("每一次揮擊打到天秤就觸發一次（還沒到受擊門檻也會播）。時間點欄位在這裡不使用，一律立刻觸發。")]
        public GreedFx hitFx = new GreedFx();

        [Header("音訊輸出")]
        [Tooltip("天秤的音效 / 配音輸出到哪個 Mixer 群組（建議 SFX，會跟著遊戲設定裡的音量走）。")]
        public AudioMixerGroup mixerGroup;

        public ScaleAnimEntry Find(string stateName)
        {
            foreach (var s in states)
                if (s != null && s.stateName == stateName) return s;
            return null;
        }

        /// <summary>某個 state 實際要播的動畫長度（有換動畫就用新的）；找不到回傳 fallback。</summary>
        public float GetLength(string stateName, float fallback)
        {
            var s = Find(stateName);
            var clip = s != null ? (s.clip != null ? s.clip : s.baseClip) : null;
            return clip != null ? clip.length : fallback;
        }
    }

    /// <summary>天秤的一個動畫 state：動畫 + 音效 / 配音 / 特效。</summary>
    [Serializable]
    public class ScaleAnimEntry
    {
        [Tooltip("Animator Controller 裡的 state 名稱（自動填入，一般不要改）。")]
        public string stateName;

        [Tooltip("這個 state 要播的動畫。換成別的 Clip 時，執行時會自動替換；長度也會跟著新動畫走（例如踢翻 / 重製完成的時間）。")]
        public AnimationClip clip;

        [Tooltip("Animator Controller 這個 state 原本綁的 Clip（自動填入，請勿修改）。")]
        public AnimationClip baseClip;

        [Tooltip("這個 state 開始播時的音效 / 配音 / 特效。時間點 = 這段動畫的比例。")]
        public GreedFx fx = new GreedFx();
    }
}
