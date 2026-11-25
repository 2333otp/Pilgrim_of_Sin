using System;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 打擊感分檔。招式／Boss 動作上選檔位，各檔實際數值集中在 HitFeedbackSettings 統一調整。
    /// Auto = 交給程式依招式種類決定（主角）；Boss 動作的 Auto 等於「無」。
    /// </summary>
    public enum HitImpactLevel
    {
        [InspectorName("自動")] Auto = 0,
        [InspectorName("無")] None = 1,
        [InspectorName("輕")] Light = 2,
        [InspectorName("中")] Medium = 3,
        [InspectorName("重")] Heavy = 4,
        [InspectorName("極重")] Extreme = 5,
    }

    /// <summary>一個檔位的全部數值。</summary>
    [Serializable]
    public class HitImpactTier
    {
        [Header("鏡頭晃動（會乘上玩家設定的晃動強度）")]
        [Min(0f)] [Tooltip("最大晃動角度（度）。只做上下左右，不做畫面傾斜。")]
        public float shakeAngle;
        [Min(0f)] [Tooltip("晃動持續秒數（由強到弱衰減）。")]
        public float shakeDuration;

        [Header("頓幀（打中時畫面停一下；不受晃動設定影響）")]
        [Min(0f)] [Tooltip("頓幀秒數（真實時間）。0 = 不停。")]
        public float hitstop;
        [Min(0f)] [Tooltip("頓幀之後的慢動作秒數（真實時間）。0 = 沒有。只建議用在擊殺 Boss。")]
        public float slowMotion;

        [Header("手把震動（玩家可在設置關閉）")]
        [Range(0f, 1f)] [Tooltip("低頻馬達（沉重感）。")]
        public float rumbleLow;
        [Range(0f, 1f)] [Tooltip("高頻馬達（清脆感）。")]
        public float rumbleHigh;
        [Min(0f)] public float rumbleDuration;

        [Header("受擊亮光（只有主角打中目標時；照原本貼圖等比例變亮，保留細節）")]
        [Min(0f)] [Tooltip("亮光秒數（一打中最亮，之後平滑退掉）。0 = 不亮。")]
        public float flashDuration;
        [Range(0f, 1f)] [Tooltip("等比例調亮：最亮時亮多少（0.2 = 亮 20%）。太高白色部位會過曝。")]
        public float flashIntensity;
        [Range(0f, 2f)] [Tooltip("暖色光暈：最亮時的光暈強度（深色部位主要靠這個看得到）。")]
        public float flashGlow;

        public HitImpactTier() { }
        public HitImpactTier(float angle, float shakeSec, float stop, float slow, float low, float high, float rumbleSec, float flash, float flashAmount, float glowAmount)
        {
            shakeAngle = angle; shakeDuration = shakeSec; hitstop = stop; slowMotion = slow;
            rumbleLow = low; rumbleHigh = high; rumbleDuration = rumbleSec;
            flashDuration = flash; flashIntensity = flashAmount; flashGlow = glowAmount;
        }
    }

    /// <summary>
    /// 打擊感設定檔：鏡頭晃動、頓幀、手把震動、受擊閃白，依「輕／中／重／極重」四檔集中調整。
    /// 掛在主角 Prefab 的 HitFeedback 元件上。
    /// </summary>
    [CreateAssetMenu(menuName = "PilgrimOfSin/打擊感設定", fileName = "HitFeedbackSettings")]
    public class HitFeedbackSettings : ScriptableObject
    {
        //                                                晃動角度 晃動秒  頓幀    慢動作  震低   震高   震秒   亮秒   調亮   光暈
        public HitImpactTier light   = new HitImpactTier(0.6f, 0.10f, 0.035f, 0f,   0.10f, 0.20f, 0.06f, 0.14f, 0.15f, 0.25f);
        public HitImpactTier medium  = new HitImpactTier(1.2f, 0.20f, 0.060f, 0f,   0.20f, 0.30f, 0.10f, 0.18f, 0.22f, 0.40f);
        public HitImpactTier heavy   = new HitImpactTier(2.2f, 0.35f, 0.100f, 0f,   0.35f, 0.45f, 0.16f, 0.24f, 0.30f, 0.60f);
        public HitImpactTier extreme = new HitImpactTier(3.2f, 0.50f, 0.120f, 0.6f, 0.55f, 0.60f, 0.30f, 0.32f, 0.40f, 0.85f);

        [Header("鏡頭晃動")]
        [Min(1f)] [Tooltip("晃動頻率（每秒幾次）。快而短的抖動比慢的搖擺不容易暈。")]
        public float shakeFrequency = 22f;
        [Range(0f, 1f)] [Tooltip("鎖定 Boss 時晃動乘上這個比例（鎖定時鏡頭本來就在轉，疊加晃動較容易暈）。")]
        public float lockOnShakeMultiplier = 0.5f;

        [Header("頓幀")]
        [Range(0.01f, 0.5f)] [Tooltip("頓幀時的時間流速。不能是 0：專案用 timeScale == 0 判斷暫停選單。")]
        public float hitstopTimeScale = 0.05f;
        [Range(0.05f, 1f)] [Tooltip("擊殺慢動作的時間流速。")]
        public float slowMotionTimeScale = 0.3f;

        [Header("Boss 重攻擊的環境晃動（沒打中也會震）")]
        [Min(0f)] [Tooltip("距離 Boss 這麼近以內全額晃動。")]
        public float environmentFullRange = 6f;
        [Min(0f)] [Tooltip("超過這個距離就不晃（中間線性遞減）。")]
        public float environmentMaxRange = 25f;

        [Header("受擊亮光")]
        [Tooltip("亮光的色調：各色版變亮的比例。暖色 = 紅多、藍少，配合手繪畫風。")]
        public Color flashTint = new Color(1f, 0.85f, 0.6f, 1f);
        [Min(0f)] [Tooltip("同一個目標兩次亮光的最短間隔（秒），連續多段攻擊時避免一直閃。")]
        public float flashMinInterval = 0.1f;

        public HitImpactTier Get(HitImpactLevel level) => level switch
        {
            HitImpactLevel.Light => light,
            HitImpactLevel.Medium => medium,
            HitImpactLevel.Heavy => heavy,
            HitImpactLevel.Extreme => extreme,
            _ => null,
        };
    }
}
