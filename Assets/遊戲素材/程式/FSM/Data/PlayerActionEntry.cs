using System;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>主角動作種類（對應 Animator state 名稱的前綴）。</summary>
    public enum PlayerActionId
    {
        Idle, Locomotion,
        LightAttack, HeavyAttack,
        Combo1, Combo2, Combo3, Combo4,
        SpecialSkill, WeaponSwitch, Damaged,
        Jump, Fall, Roll, Death,
    }

    /// <summary>單一特效設定。Prefab 留空 = 整筆略過，不會報錯。</summary>
    [Serializable]
    public class PlayerVfxEntry
    {
        [Tooltip("特效 Prefab。留空就不生成。")]
        public GameObject prefab;

        [Tooltip("生成位置的骨骼名稱（例如 DEF-hand_attach.R）。留空 = 角色根物件。找不到骨骼時退回角色根物件。")]
        public string boneName;

        [Tooltip("位置偏移（以角色朝向為基準的世界尺寸，單位公尺；不受骨架 100 倍縮放影響）。")]
        public Vector3 positionOffset;

        [Tooltip("旋轉偏移（歐拉角，以角色朝向為基準）。")]
        public Vector3 rotationOffset;

        [Range(0f, 1f)]
        [Tooltip("觸發時間點：動作開始後經過動畫長度的比例（0 = 一開始、0.5 = 動畫一半）。沒有動畫長度可參考時視為立刻生成。")]
        public float normalizedTime;

        [Tooltip("勾選：生成後掛在骨骼底下跟著動；不勾：留在生成的位置。")]
        public bool followBone;

        [Min(0f)]
        [Tooltip("幾秒後自動刪除特效物件。0 = 不自動刪除（請確保 Prefab 自己會消失）。")]
        public float lifetime = 3f;
    }

    /// <summary>
    /// 一個動作的全部素材：動畫、音效、配音、特效。對應 Animator 裡一個 state（例如 LightAttack_Pencil）。
    /// 第一個欄位是 stateName，Inspector 清單會以它當元素標題。
    /// </summary>
    [Serializable]
    public class PlayerActionEntry
    {
        [Tooltip("Animator Controller 裡的 state 名稱，程式用它對應這筆資料。一般不要改。")]
        public string stateName;

        public PlayerActionId action;

        [Header("動畫")]
        [Tooltip("這個動作要播的動畫。換成別的 Clip，動作時間會自動跟著 Clip 長度走。\n" +
                 "只在 Animator 裡已經有這個 state 時有效；要新增 state 請先在 Animator Controller 建好。")]
        public AnimationClip clip;

        [Tooltip("Animator Controller 這個 state 原本綁的 Clip（自動填入，請勿修改）。Clip 欄位跟它不同時，執行時會把它換掉。")]
        public AnimationClip baseClip;

        [Min(0f)]
        [Tooltip("動作時間 = Clip 長度 + 這個餘裕（秒）。預設 0；翻滾、受擊硬直原本多留一點餘裕，已自動填入。")]
        public float endPadding;

        [Min(0f)]
        [Tooltip("保險時間（秒）：Animator 讀不到這個動作的動畫（例如還沒做動畫的武器）時使用。0 = 用程式內建值。")]
        public float fallbackDuration;

        [Tooltip("音效清單，可任意增減，每次隨機播放其中一個。留空 = 不播。")]
        public SoundEffectData[] sfx = new SoundEffectData[0];

        [Range(0f, 1f)]
        [Tooltip("音效播放時間點：動作開始後經過動畫長度的比例。0 = 動作一開始就播，0.3 = 動畫播到 30% 才播。沒有動畫長度可參考時視為立刻播。")]
        public float sfxNormalizedTime;

        [Tooltip("配音清單，可任意增減，每次隨機播放其中一個。留空 = 不播。")]
        public SoundEffectData[] voice = new SoundEffectData[0];

        [Range(0f, 1f)]
        [Tooltip("配音播放時間點，用法同音效。")]
        public float voiceNormalizedTime;

        [Tooltip("特效清單，可多個；Prefab 留空 = 不生成。")]
        public PlayerVfxEntry[] vfx = new PlayerVfxEntry[0];

        /// <summary>有放 Clip 且 Animator 裡確實有這個 state，才能用 Clip 長度決定動作時間。</summary>
        public bool HasClipDuration => clip != null && baseClip != null;
        public float ClipDuration => clip.length + endPadding;
    }
}
