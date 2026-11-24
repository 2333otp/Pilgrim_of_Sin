using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 貪的素材總表：共通欄位（基本資料、基本動作、受擊…）見 BossProfileBase，格式和癡相同；
    /// 這裡另外放貪的招式清單（含派生）與平衡結束空襲流程。企劃直接在 Assets/遊戲素材/SO/Boss 底下調整，不用開場景。
    /// 新增招式：在招式清單加一筆、放動畫 → 按 Inspector 最下面的「同步到 Animator」建立對應的 Animator state。
    /// </summary>
    [CreateAssetMenu(fileName = "GreedBossProfile", menuName = "PilgrimOfSin/Boss/貪素材總表 (Greed Boss Profile)")]
    public class GreedBossProfile : BossProfileBase
    {
        [Header("受擊（貪）")]
        [Min(0f)]
        [Tooltip("兩次受擊硬直的最短間隔（秒）。避免連段把 Boss 鎖死在受擊動作；攻擊 / 空襲中不會被打斷（霸體）。")]
        public float staggerCooldown = 4f;

        [Header("招式（貪）")]
        [Tooltip("Boss 會用的所有招式。勾「可當起手招」的才會在待機時被挑選；其他招只能被派生接出來。\n" +
                 "名稱 = Animator state 名稱，也是派生指定要接哪一招用的名字。")]
        [BossEntryHide("attackRange")]
        public List<GreedMoveEntry> moves = new List<GreedMoveEntry>();

        [Min(0f)]
        [Tooltip("招式之間的轉場混合秒數（CrossFade）。")]
        public float crossFade = 0.1f;

        [Header("平衡結束：起飛 → 定位天秤一側 → 踢翻 → 鎖定主角墜落")]
        public GreedAerialKickConfig aerialKick = new GreedAerialKickConfig();

        private void Reset()
        {
            displayName = "心魔-貪";
            maxHp = 5000f;
            moveSpeed = 4f;
            // 貪的 Animator 裡基本動作的 state 名稱
            idle.stateName = "Idle";
            move.stateName = "Run";
            hurt.stateName = "Damage";
            dead.stateName = "Death";
            staggerDuration = 0f;
        }

        public override IEnumerable<BossActionEntry> AllEntries()
        {
            foreach (var e in base.AllEntries()) yield return e;
            foreach (var m in moves) if (m != null) yield return m;
            yield return aerialKick.fly;
            yield return aerialKick.kick;
            yield return aerialKick.dive;
            yield return aerialKick.fall;
        }

        public GreedMoveEntry FindMove(string stateName)
        {
            if (string.IsNullOrEmpty(stateName)) return null;
            foreach (var m in moves)
                if (m != null && m.stateName == stateName) return m;
            return null;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  條件
    // ════════════════════════════════════════════════════════════════

    /// <summary>天秤相位遮罩（可複選）。</summary>
    [Flags]
    public enum GreedPhaseMask
    {
        雕像重 = 1 << 0,
        平衡 = 1 << 1,
        錢袋重 = 1 << 2,
        踢翻中 = 1 << 3,
    }

    /// <summary>出招 / 派生共用的條件：距離、天秤相位、Boss 血量比例。全部成立才算數。</summary>
    [Serializable]
    public class GreedMoveCondition
    {
        [Min(0f)]
        [Tooltip("和主角的水平距離 ≥ 這個值（公尺）。")]
        public float minDistance;

        [Min(0f)]
        [Tooltip("和主角的水平距離 ≤ 這個值（公尺）。0 = 不限。")]
        public float maxDistance;

        [Tooltip("天秤在這些相位時才成立（可複選）。全不選 = 不限。")]
        public GreedPhaseMask phases = (GreedPhaseMask)~0;

        [Range(0f, 1f)]
        [Tooltip("Boss 血量比例 ≥ 這個值。")]
        public float hpMin;

        [Range(0f, 1f)]
        [Tooltip("Boss 血量比例 ≤ 這個值。")]
        public float hpMax = 1f;

        public bool Evaluate(float distance, ScalePhase phase, float hpRatio)
        {
            if (distance < minDistance) return false;
            if (maxDistance > 0f && distance > maxDistance) return false;
            if (phases != 0 && (phases & ToMask(phase)) == 0) return false;
            if (hpRatio < hpMin || hpRatio > hpMax) return false;
            return true;
        }

        public static GreedPhaseMask ToMask(ScalePhase p) => p switch
        {
            ScalePhase.StatueHeavy => GreedPhaseMask.雕像重,
            ScalePhase.Balanced => GreedPhaseMask.平衡,
            ScalePhase.MoneyBagHeavy => GreedPhaseMask.錢袋重,
            _ => GreedPhaseMask.踢翻中,
        };
    }

    // ════════════════════════════════════════════════════════════════
    //  一招
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// 貪的一招 = 共通動作格式（BossActionEntry：名稱、動畫、音效、配音、特效、傷害、單次扇形判定）
    /// + 貪專用的起手條件、瞄準突進、多段命中區間、派生。
    /// </summary>
    [Serializable]
    public class GreedMoveEntry : BossActionEntry
    {
        [Header("播放")]
        [Min(0.1f)]
        [Tooltip("播放速度。1 = 原速。動作長度 = 動畫長度 ÷ 播放速度 + 結尾餘裕。")]
        public float speed = 1f;

        [Header("多段命中區間（貪）")]
        [Tooltip("多段攻擊：每一格 = 一下攻擊，動畫播到 開始~結束（比例）之間有球形判定，每一下最多打中主角一次。\n" +
                 "有填任何一格時，上面的單次扇形判定（命中時間點 / 距離 / 角度）就不使用。")]
        public List<GreedHitWindow> hitWindows = new List<GreedHitWindow>();

        [Header("起手（待機時挑選）")]
        [Tooltip("勾選 = 待機時可以直接出這招。不勾 = 只能被其他招派生出來。")]
        public bool canOpen = true;

        [Tooltip("起手條件（距離 / 相位 / 血量）。")]
        public GreedMoveCondition openerCondition = new GreedMoveCondition { maxDistance = 3f };

        [Min(0f)]
        [Tooltip("起手權重：同時有多招符合條件時，照權重隨機挑。")]
        public float openerWeight = 1f;

        [Min(0f)]
        [Tooltip("冷卻秒數：這招出完後多久內不會再被選為起手招（派生不受影響）。")]
        public float cooldown;

        [Header("瞄準與位移")]
        [Range(0f, 1f)]
        [Tooltip("動畫播到這個比例之前持續轉向主角（之後方向鎖定）。")]
        public float trackUntilRatio = 0.5f;

        [Min(0f)]
        [Tooltip("轉向速度。")]
        public float turnSpeed = 10f;

        [Tooltip("突進：動畫播到 開始~結束 比例之間往前移動的總距離（公尺）。0 = 原地。會被障礙物和主角擋住。")]
        public float lungeDistance;
        [Range(0f, 1f)] public float lungeStart = 0.3f;
        [Range(0f, 1f)] public float lungeEnd = 0.6f;

        [Header("收招")]
        [Min(0f)]
        [Tooltip("這招播完（沒有派生）後回待機，待機多久再選下一招。")]
        public float recoveryIdle = 1f;

        [Header("派生（由上往下檢查，第一個成立的就接）")]
        public List<GreedMoveBranch> branches = new List<GreedMoveBranch>();

        public GreedMoveEntry() { stateName = "Attack1"; damage = 900f; }

        public bool IsValid => EffectiveClip != null && !string.IsNullOrEmpty(stateName);
        /// <summary>沒有命中區間、但有填單次判定距離時，用和癡相同的扇形判定。</summary>
        public bool UsesFanHit => (hitWindows == null || hitWindows.Count == 0) && hitRadius > 0f;
        public float Duration => EffectiveClip != null ? EffectiveClip.length / Mathf.Max(0.1f, speed) + endPadding : fallbackDuration;
    }

    /// <summary>
    /// 只有音效 / 配音 / 特效、沒有動畫的一組設定（目前給天秤 GreedScaleConfig 用；Boss 的動作一律用 BossActionEntry）。
    /// 欄位和 BossActionEntry 的音效 / 配音 / 特效段相同。全部留空 = 什麼都不播，不會報錯。
    /// 時間點是「這段動畫的比例」，依動畫實際播放進度觸發（停格時也會正確等待）。
    /// </summary>
    [Serializable]
    public class GreedFx : IBossFx
    {
        AudioClip[] IBossFx.Sfx => sfx;
        float IBossFx.SfxVolume => sfxVolume;
        float IBossFx.SfxNormalizedTime => sfxNormalizedTime;
        float IBossFx.VoiceVolume => voiceVolume;
        float IBossFx.VoiceNormalizedTime => voiceNormalizedTime;
        PlayerVfxEntry[] IBossFx.Vfx => vfx;

        [Tooltip("音效音檔，可放多個，每次隨機播一個。留空 = 不播。")]
        public AudioClip[] sfx = new AudioClip[0];
        [Range(0f, 1f)] public float sfxVolume = 1f;
        [Range(0f, 1f)]
        [Tooltip("音效播放時間點（動畫比例）。0 = 一開始就播。")]
        public float sfxNormalizedTime;

        [Tooltip("配音（英文版），可放多個，每次隨機播一個。語言設為英文時才會播。")]
        public AudioClip[] voiceEnglish = new AudioClip[0];
        [Tooltip("配音（台語版），可放多個，每次隨機播一個。語言設為台語時才會播。")]
        public AudioClip[] voiceTaiwanese = new AudioClip[0];
        [Range(0f, 1f)] public float voiceVolume = 1f;
        [Range(0f, 1f)]
        [Tooltip("配音播放時間點（動畫比例）。")]
        public float voiceNormalizedTime;

        [Tooltip("特效清單，可多個；Prefab 留空 = 不生成。骨骼名稱留空 = Boss 根物件。")]
        public PlayerVfxEntry[] vfx = new PlayerVfxEntry[0];

        public AudioClip[] GetVoice(VoiceLanguage language)
            => language == VoiceLanguage.English ? voiceEnglish : voiceTaiwanese;
    }

    /// <summary>一招裡的一下攻擊判定（球形）。</summary>
    [Serializable]
    public class GreedHitWindow
    {
        [Range(0f, 1f)] public float start = 0.75f;
        [Range(0f, 1f)] public float end = 0.85f;

        [Min(0f)]
        [Tooltip("這一下的傷害。0 = 整招傷害扣掉有填的之後平分。")]
        public float damage;

        [Tooltip("判定球中心：Boss 前方距離（m）")]
        public float forward = 1.3f;
        [Tooltip("判定球中心：離地高度（m）")]
        public float height = 1f;
        [Min(0.05f)]
        [Tooltip("判定球半徑（m）")]
        public float radius = 1.5f;

        public static float DamageAt(List<GreedHitWindow> windows, int i, float total)
        {
            if (windows == null || i < 0 || i >= windows.Count) return total;
            if (windows[i].damage > 0f) return windows[i].damage;
            float fixedSum = 0f; int open = 0;
            foreach (var w in windows)
            {
                if (w.damage > 0f) fixedSum += w.damage;
                else open++;
            }
            return open > 0 ? Mathf.Max(0f, total - fixedSum) / open : 0f;
        }
    }

    /// <summary>派生：這一招播到某個比例時，條件成立就依機率接另一招。</summary>
    [Serializable]
    public class GreedMoveBranch
    {
        [Tooltip("要接的招式名稱（招式清單裡的 id）。")]
        public string toMove;

        [Range(0f, 1f)]
        [Tooltip("動畫播到這個比例時檢查（同時也是切換的時間點）。1 = 播完才接。")]
        public float atRatio = 0.9f;

        [Range(0f, 1f)]
        [Tooltip("條件成立時，接這招的機率。")]
        public float chance = 1f;

        public GreedMoveCondition condition = new GreedMoveCondition();
    }

    // ════════════════════════════════════════════════════════════════
    //  平衡結束空襲
    // ════════════════════════════════════════════════════════════════

    public enum GreedOrbitDirection
    {
        自動,     // 選總角度比較少的方向
        順時針,
        逆時針,
    }

    public enum GreedKickSide
    {
        離Boss近的那側,
        離主角遠的那側,
        離主角近的那側,
        隨機,
    }

    [Serializable]
    public class GreedAerialKickConfig
    {
        [Header("起飛 (Fly)")]
        [Tooltip("起飛：動畫 + 音效 / 配音 / 特效（時間點 = 這段動畫的比例，停格時會等待）。例如振翅聲、起飛嘶吼。")]
        [BossEntryHide] public BossActionEntry fly = new BossActionEntry(GreedBossController.AnimStateFly);
        [Min(0.1f)] public float flySpeedMultiplier = 1f;

        [Range(0f, 1f)]
        [Tooltip("起飛動畫播到這個比例才離地開始移動。")]
        public float liftOffRatio = 0.8f;

        [Range(0f, 1f)]
        [Tooltip("停格幀：起飛動畫播到這裡就停住，直到飛到天秤旁的定位點才繼續播完。")]
        public float flyHoldRatio = 0.9f;

        [Range(0f, 1f)]
        [Tooltip("盤旋時的循環區段終點：> 停格幀時，盤旋期間在「停格幀 ~ 這裡」來回播（例如滑翔時翅膀微微擺動）。\n≤ 停格幀 = 完全停格。")]
        public float flyLoopEndRatio = 0f;

        [Min(0.1f)]
        [Tooltip("飛行速度（m/s，沿路徑）。")]
        public float flySpeed = 12f;

        [Header("盤旋（像老鷹 / 遊隼繞著天秤迴旋爬升，再滑翔切入定位點）")]
        [Tooltip("盤旋高度（離地 m）。")]
        public float cruiseHeight = 11f;

        [Min(1f)]
        [Tooltip("盤旋半徑（以天秤為中心，m）。天秤寬約 8m，場地牆約在 20m。")]
        public float orbitRadius = 10f;

        [Min(0f)]
        [Tooltip("最少要繞幾度才切入定位點（360 = 至少一整圈）。實際角度會再加上到定位點那一側需要的角度。")]
        public float orbitDegrees = 300f;

        public GreedOrbitDirection orbitDirection = GreedOrbitDirection.自動;

        [Range(0f, 60f)]
        [Tooltip("轉彎時身體往內側傾斜的最大角度。")]
        public float bankAngle = 30f;

        [Range(0.05f, 0.5f)]
        [Tooltip("路徑前段多少比例用來爬升 + 拉開到盤旋半徑。")]
        public float climbPortion = 0.3f;

        [Range(0.05f, 0.5f)]
        [Tooltip("路徑後段多少比例用來滑翔下降 + 切入定位點。")]
        public float glidePortion = 0.3f;

        [Header("定位：天秤的一側")]
        [Tooltip("定位點（以天秤為原點的本地座標）。X 會左右鏡像產生兩個候選點，Y = 踢翻時的離地高度。")]
        public Vector3 kickPointLocal = new Vector3(5.5f, 5f, 0f);

        public GreedKickSide kickSide = GreedKickSide.離Boss近的那側;

        [Header("踢翻 (Kick)")]
        [Tooltip("踢翻：例如踢擊聲、喊聲（時間點可對到下面的踢中幀）。")]
        [BossEntryHide] public BossActionEntry kick = new BossActionEntry(GreedBossController.AnimStateKick);
        [Min(0.1f)] public float kickSpeedMultiplier = 1f;

        [Range(0f, 1f)]
        [Tooltip("踢中天秤的幀：播到這裡時天秤開始播 Break（翻倒）並開啟天秤傷害碰撞體。")]
        public float kickImpactRatio = 0.55f;

        [Header("墜落：俯衝 (Dive) → 落地 (Fall)")]
        [Tooltip("俯衝：例如破風聲、俯衝嘶吼。")]
        [BossEntryHide] public BossActionEntry dive = new BossActionEntry(GreedBossController.AnimStateDive);
        [Min(0.1f)] public float diveSpeedMultiplier = 1f;

        [Range(0f, 1f)]
        [Tooltip("俯衝動畫播到這個比例才開始往主角衝（之前是在空中蓄勢、持續瞄準）。")]
        public float diveLaunchRatio = 0.3f;

        [Range(0f, 1f)]
        [Tooltip("停格幀：俯衝動畫播到這裡就停住，直到落地才接落地動畫。")]
        public float diveHoldRatio = 0.4f;

        [Min(0.1f)]
        [Tooltip("俯衝速度（m/s）。")]
        public float diveSpeed = 16f;

        [Tooltip("勾選 = 在開始俯衝那一刻才鎖定主角座標；不勾 = 踢完天秤那一刻就鎖定。")]
        public bool lockTargetAtLaunch = true;

        [Min(0f)]
        [Tooltip("落點最多離主角腳下多遠會被預判修正。0 = 直接砸在鎖定時的座標。")]
        public float maxLeadDistance;

        [Tooltip("落地 + 趴地爬起：時間點 0 = 撞地那一刻，適合放撞擊聲、煙塵 / 震波特效。")]
        [BossEntryHide] public BossActionEntry fall = new BossActionEntry(GreedBossController.AnimStateFall);
        [Min(0.1f)] public float fallSpeedMultiplier = 1f;

        [Header("落地衝擊")]
        [Min(0f)] public float impactDamage = 1500f;
        [Min(0f)] public float impactRadius = 3f;

        [Header("保險")]
        [Min(1f)]
        [Tooltip("任何一段飛行超過這麼多秒還沒到定位就強制瞬移到位（避免卡死）。")]
        public float maxTravelSeconds = 15f;

        /// <summary>四段動畫都有放才走空襲流程；缺任何一段就退回舊流程。</summary>
        public bool IsComplete => fly.EffectiveClip && kick.EffectiveClip && dive.EffectiveClip && fall.EffectiveClip;

        public float FlyDuration => Len(fly, flySpeedMultiplier);
        public float KickDuration => Len(kick, kickSpeedMultiplier);
        public float DiveDuration => Len(dive, diveSpeedMultiplier);
        public float FallDuration => Len(fall, fallSpeedMultiplier);

        private static float Len(BossActionEntry e, float speed)
        {
            var c = e?.EffectiveClip;
            return c ? c.length / Mathf.Max(0.1f, speed) + e.endPadding : 0f;
        }
    }
}
