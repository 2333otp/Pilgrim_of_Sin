using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 所有 Boss 素材總表的共通格式（癡 FoolishBossProfile、貪 GreedBossProfile 都繼承它）。
    /// 每隻 Boss 都有：基本資料、Animator、待機 / 移動 / 受擊 / 死亡四個基本動作、受擊設定、測試開關、音訊輸出；
    /// 各自的招式與機制寫在子類別裡。
    /// Inspector 順序：共通欄位 → 各 Boss 專屬欄位 → 測試 / 音訊輸出（見 BossProfileEditor）。
    /// </summary>
    public abstract class BossProfileBase : ScriptableObject
    {
        [Header("基本資料")]
        [Tooltip("血條旁顯示的名字。")]
        public string displayName = "心魔";
        [Min(1f)] public float maxHp = 10000f;
        [Min(0f)] [Tooltip("移動（追主角）速度，m/s。")]
        public float moveSpeed = 3.5f;
        [Min(0.1f)]
        [Tooltip("移動動畫本身對應的速度（m/s，腳不滑的速度）。移動時動畫播放速度 = 移動速度 ÷ 這個值，避免在地上滑。\n" +
                 "例：動畫原本是慢走（約 1 m/s）、移動速度 3.5 → 動畫以 3.5 倍速播放。")]
        public float moveAnimReferenceSpeed = 1f;
        [Min(0f)] [Tooltip("每次攻擊或移動之後待機（決策）幾秒。")]
        public float idleDuration = 1f;

        [Header("動畫")]
        [Tooltip("這隻 Boss 的 Animator Controller。Inspector 最下面的按鈕會從這裡讀出每個動作原本綁的動畫（原始 Clip）。")]
        public RuntimeAnimatorController animatorController;

        [Header("基本動作素材")]
        [BossEntryHide] public BossActionEntry idle = new BossActionEntry("Idle");
        [Tooltip("移動（走向主角）。")]
        [BossEntryHide] public BossActionEntry move = new BossActionEntry("Move");
        [Tooltip("受擊：每次被打都會播音效 / 特效（配音有最小間隔）；動畫只在「受擊硬直」勾選時才播。")]
        [BossEntryHide] public BossActionEntry hurt = new BossActionEntry("Stagger");
        [BossEntryHide] public BossActionEntry dead = new BossActionEntry("Dead");

        [Header("受擊")]
        [Min(0f)] [Tooltip("受擊配音的最小間隔（秒）。連續被打時避免每一下都喊（音效、特效不受限）。")]
        public float hurtVoiceMinInterval = 1.5f;
        [Tooltip("勾選：被打時會中斷動作並播受擊動畫（硬直）。")]
        public bool enableStagger = false;
        [Min(0f)] [Tooltip("受擊硬直時間（秒）。0 = 依受擊動畫長度。")]
        public float staggerDuration = 0.5f;

        [Header("測試")]
        [Tooltip("勾選：每次攻擊命中判定時，在場景裡閃一下判定範圍（紅 = 打到、黃 = 沒打到），並在 Console 印出時間點。" +
                 "用來邊玩邊調命中判定，正式版記得取消勾選。")]
        public bool showHitArea = false;

        [Header("音訊輸出")]
        [Tooltip("音效與配音要輸出到哪個 Mixer 群組（建議跟主角一樣指到 SFX，會跟著遊戲設定裡的音量走）。")]
        public AudioMixerGroup mixerGroup;

        /// <summary>移動動畫的播放倍率（給 Animator 的 MoveSpeed 參數）。</summary>
        public float MoveAnimSpeed => moveAnimReferenceSpeed > 0f ? moveSpeed / moveAnimReferenceSpeed : 1f;

        /// <summary>受擊硬直實際秒數（0 = 用受擊動畫長度）。</summary>
        public float StaggerTime => staggerDuration > 0f ? staggerDuration : hurt.GetDuration(0.5f);

        /// <summary>這份總表裡所有對應 Animator state 的動作（換動畫、讀原始 Clip 用）。子類別要把自己的招式也加進來。</summary>
        public virtual IEnumerable<BossActionEntry> AllEntries()
        {
            yield return idle;
            yield return move;
            yield return hurt;
            yield return dead;
        }
    }
}
