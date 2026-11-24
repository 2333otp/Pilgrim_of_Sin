using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 癡的素材總表：動畫、音效、配音、特效、攻擊數值、場地機制全部集中在這裡，換素材或調難度只改這份，不用動程式。
    /// 掛在 FoolishBossController 的 Profile 欄位。共通欄位（基本資料、基本動作、受擊…）見 BossProfileBase。
    /// 每個動作對應 Foolish_Gameplay.controller 裡同名的 state（Idle / Move / Attack1~3 / Roar / Stagger / Dead）。
    /// </summary>
    [CreateAssetMenu(fileName = "FoolishBossProfile", menuName = "PilgrimOfSin/Boss/癡素材總表 (Foolish Boss Profile)")]
    public class FoolishBossProfile : BossProfileBase
    {
        [Header("招式（癡）")]
        [Tooltip("攻擊1：近距離。")]
        public BossActionEntry attack1 = new BossActionEntry("Attack1");
        [Tooltip("攻擊2：中距離。")]
        public BossActionEntry attack2 = new BossActionEntry("Attack2");
        [Tooltip("攻擊3：遠距離。")]
        public BossActionEntry attack3 = new BossActionEntry("Attack3");
        [Tooltip("嘶吼：打鬥窗口結束、惡區即將重新出現的信號（目前用 attack4 的動畫）。")]
        [BossEntryHide] public BossActionEntry roar = new BossActionEntry("Roar");

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

        private void Reset()
        {
            displayName = "心魔-癡";
        }

        public override IEnumerable<BossActionEntry> AllEntries()
        {
            foreach (var e in base.AllEntries()) yield return e;
            yield return attack1;
            yield return attack2;
            yield return attack3;
            yield return roar;
        }

        public BossActionEntry GetAttack(FoolishBossStateType type) => type switch
        {
            FoolishBossStateType.Attack1 => attack1,
            FoolishBossStateType.Attack2 => attack2,
            _ => attack3,
        };
    }
}
