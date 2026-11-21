using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 癡的素材總表：動畫、音效、配音、特效、攻擊數值、場地機制全部集中在這裡，換素材或調難度只改這份，不用動程式。
    /// 掛在 FoolishBossController 的 Profile 欄位。
    /// 每個動作對應 Foolish_Gameplay.controller 裡同名的 state（Idle / Move / Attack1~3 / Roar / Stagger / Dead）。
    /// </summary>
    [CreateAssetMenu(fileName = "FoolishBossProfile", menuName = "PilgrimOfSin/Boss/癡素材總表 (Foolish Boss Profile)")]
    public class FoolishBossProfile : ScriptableObject
    {
        [Header("基本資料")]
        public string displayName = "心魔-癡";
        [Min(1f)] public float maxHp = 10000f;
        [Min(0f)] public float moveSpeed = 3.5f;
        [Min(0f)] [Tooltip("每次攻擊或移動之後待機（決策）幾秒。")]
        public float idleDuration = 1f;

        [Header("動作素材")]
        public BossActionEntry idle = new BossActionEntry("Idle");
        [Tooltip("移動（走向主角）。")]
        public BossActionEntry move = new BossActionEntry("Move");
        [Tooltip("攻擊1：近距離。")]
        public BossActionEntry attack1 = new BossActionEntry("Attack1");
        [Tooltip("攻擊2：中距離。")]
        public BossActionEntry attack2 = new BossActionEntry("Attack2");
        [Tooltip("攻擊3：遠距離。")]
        public BossActionEntry attack3 = new BossActionEntry("Attack3");
        [Tooltip("嘶吼：打鬥窗口結束、惡區即將重新出現的信號（目前用 attack4 的動畫）。")]
        public BossActionEntry roar = new BossActionEntry("Roar");
        [Tooltip("受擊：每次被打都會播音效/配音/特效；動畫只在「受擊硬直」勾選時才播。")]
        public BossActionEntry hurt = new BossActionEntry("Stagger");
        public BossActionEntry dead = new BossActionEntry("Dead");

        [Header("受擊")]
        [Min(0f)] [Tooltip("受擊配音的最小間隔（秒）。連續被打時避免每一下都喊（音效、特效不受限）。")]
        public float hurtVoiceMinInterval = 1.5f;
        [Tooltip("勾選：被打時會中斷動作並播受擊動畫（硬直）。")]
        public bool enableStagger = false;
        [Min(0f)] [Tooltip("受擊硬直時間（秒）。0 = 依受擊動畫長度。")]
        public float staggerDuration = 0.5f;

        [Header("攻擊保險時間")]
        [Min(0.1f)] [Tooltip("攻擊動作沒有動畫可以量長度時，最多持續幾秒就結束，避免卡死。")]
        public float attackFallbackDuration = 1.5f;

        [Header("模型尺寸")]
        [Tooltip("勾選：開始時自動量模型實際高度，縮放到下面的目標高度並讓腳貼地。預設不勾，維持模型原始尺寸。")]
        public bool autoFitModel = false;
        [Min(0.1f)] [Tooltip("目標身高（公尺）。規則文件：癡體型 300cm。只有勾選自動縮放才會用到。")]
        public float modelTargetHeight = 3f;
        [Tooltip("勾選：開始時依模型實際大小調整碰撞膠囊（不改變模型本身大小）。")]
        public bool autoFitCollider = true;

        [Header("場地機制（善惡區循環、惡區傷害、安全區）")]
        public FoolishArenaSettings arena = new FoolishArenaSettings();

        [Header("測試")]
        [Tooltip("勾選：每次攻擊命中判定時，在地上閃一下判定範圍（紅 = 打到、黃 = 沒打到），並在 Console 印出時間點。" +
                 "用來邊玩邊調「命中判定時間點／距離／角度」，正式版記得取消勾選。")]
        public bool showHitArea = false;

        [Header("音訊輸出")]
        [Tooltip("音效與配音要輸出到哪個 Mixer 群組（建議跟主角一樣指到 SFX，會跟著遊戲設定裡的音量走）。")]
        public AudioMixerGroup mixerGroup;

        public IEnumerable<BossActionEntry> AllEntries()
        {
            yield return idle;
            yield return move;
            yield return attack1;
            yield return attack2;
            yield return attack3;
            yield return roar;
            yield return hurt;
            yield return dead;
        }

        public BossActionEntry GetAttack(FoolishBossStateType type) => type switch
        {
            FoolishBossStateType.Attack1 => attack1,
            FoolishBossStateType.Attack2 => attack2,
            _ => attack3,
        };
    }
}
