using System;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 癡的場地機制設定（善惡區循環、惡區傷害、安全區）。數值全部在 FoolishBossController 的 Inspector 可調。
    /// </summary>
    [Serializable]
    public class FoolishArenaSettings
    {
        [Header("循環時間（秒）")]
        [Tooltip("善/惡區出現的時間。")]
        [Min(0.1f)] public float zoneShowDuration = 5f;
        [Tooltip("每輪善惡區之後的正常地板緩衝。")]
        [Min(0f)] public float bufferDuration = 2.5f;
        [Tooltip("一個大循環裡善惡區出現幾輪（規則：2 輪）。")]
        [Min(1)] public int zoneRounds = 2;
        [Tooltip("打鬥窗口時間。")]
        [Min(0.1f)] public float battleDuration = 10f;
        [Tooltip("打鬥窗口結束後癡嘶吼的時間。")]
        [Min(0f)] public float roarDuration = 1f;
        [Tooltip("嘶吼後地面從邊緣擴散變黑的時間（這段時間還不會扣血）。")]
        [Min(0f)] public float spreadDuration = 2f;

        [Header("打鬥窗口")]
        [Tooltip("打鬥窗口期間癡血量降到這個比例以下，窗口提前結束。每場戰鬥只觸發一次（之後的窗口維持正常長度）。")]
        [Range(0f, 1f)] public float battleEarlyEndHpRatio = 0.4f;
        [Tooltip("勾選：躲避階段癡也可以被打（預設不行，只有打鬥窗口能造成傷害）。")]
        public bool vulnerableOutsideBattle = false;

        [Header("惡區傷害（每秒）：依癡的血量分三段")]
        [Tooltip("癡血量高於這個比例：使用 dpsHigh。")]
        [Range(0f, 1f)] public float tierHighHpRatio = 0.5f;
        [Tooltip("癡血量高於這個比例（且不高於上一段）：使用 dpsMid；再低就用 dpsLow。")]
        [Range(0f, 1f)] public float tierMidHpRatio = 0.25f;
        [Min(0f)] public float dpsHigh = 280f;
        [Min(0f)] public float dpsMid = 200f;
        [Min(0f)] public float dpsLow = 120f;

        [Header("安全區")]
        [Tooltip("每輪安全區數量最少/最多（假隨機）。最多不會超過場景裡 SafeZone 物件數量。")]
        [Min(1)] public int safeZoneMin = 1;
        [Min(1)] public int safeZoneMax = 3;
        [Tooltip("數量是最少或最多時，連續幾輪相同後就強制換（暗自保底）。")]
        [Min(1)] public int maxSameCountStreak = 2;
        [Tooltip("安全區基本半徑。0 = 使用場景裡 SafeZone 物件目前的大小。")]
        [Min(0f)] public float safeZoneBaseRadius = 0f;
        [Tooltip("癡血量從 100% 降到 0% 時，安全區半徑額外增加多少（線性向外擴張，光明擴散感）。0 = 大小固定。")]
        [Min(0f)] public float safeZoneExtraRadiusAtZeroHp = 4f;
        [Tooltip("安全區彼此之間至少隔多遠（邊到邊）。")]
        [Min(0f)] public float safeZoneGap = 2f;

        [Header("場地（方形，以世界座標 XZ 計算）")]
        public Vector2 arenaCenter = Vector2.zero;
        [Tooltip("場地邊長的一半。目前地板 Plane 縮放 7 = 70x70，所以是 35。")]
        [Min(1f)] public float arenaHalfSize = 35f;
        [Tooltip("安全區整個圓都要離地圖邊緣至少這麼遠，不貼邊生成。")]
        [Min(0f)] public float edgeMargin = 5f;

        /// <summary>依癡目前血量比例取得惡區每秒傷害。</summary>
        public float GetEvilDps(float hpRatio)
        {
            if (hpRatio > tierHighHpRatio) return dpsHigh;
            if (hpRatio > tierMidHpRatio) return dpsMid;
            return dpsLow;
        }
    }
}
