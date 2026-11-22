using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 貪 Boss 的招式表與平衡結束空襲流程設定。企劃直接在 Assets/遊戲素材/SO 底下調整，不用開場景。
    /// 新增招式：在招式清單加一筆、放動畫 → 按 Inspector 最下面的「同步到 Animator」建立對應的 Animator state。
    /// </summary>
    [CreateAssetMenu(fileName = "GreedBossProfile", menuName = "PilgrimOfSin/貪/Boss 招式表 (Greed Boss Profile)")]
    public class GreedBossProfile : ScriptableObject
    {
        [Tooltip("這個 Profile 綁定的 Animator Controller（「同步到 Animator」會把招式動畫寫進這裡）。")]
        public RuntimeAnimatorController animatorController;

        [Header("招式清單")]
        [Tooltip("Boss 會用的所有招式。勾「可當起手招」的才會在待機時被挑選；其他招只能被派生接出來。")]
        public List<GreedMoveEntry> moves = new List<GreedMoveEntry>();

        [Min(0f)]
        [Tooltip("招式之間的轉場混合秒數（CrossFade）。")]
        public float crossFade = 0.1f;

        [Header("平衡結束：起飛 → 定位天秤一側 → 踢翻 → 鎖定主角墜落")]
        public GreedAerialKickConfig aerialKick = new GreedAerialKickConfig();

        [Header("音訊輸出")]
        [Tooltip("Boss 的音效 / 配音輸出到哪個 Mixer 群組（建議和主角相同，會跟著遊戲設定裡的音量走）。")]
        public AudioMixerGroup mixerGroup;

        [Header("受擊 / 死亡（時間點欄位在這裡不使用，一律立刻觸發）")]
        public GreedFx hurtFx = new GreedFx();

        [Min(0f)]
        [Tooltip("受擊音效 / 配音 / 特效的最短間隔（秒），避免連段時每一下都喊。")]
        public float hurtMinInterval = 1.5f;

        public GreedFx deathFx = new GreedFx();

        public GreedMoveEntry FindMove(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var m in moves)
                if (m != null && m.id == id) return m;
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

    [Serializable]
    public class GreedMoveEntry
    {
        [Tooltip("招式名稱（派生用這個名字指定要接哪一招，也是 Animator state 名稱）。不要重複。")]
        public string id = "Attack1";

        [Tooltip("這一招播的動畫。動作長度 = 動畫長度 ÷ 播放速度。改了動畫記得按「同步到 Animator」。")]
        public AnimationClip clip;

        [Min(0.1f)]
        [Tooltip("播放速度。1 = 原速。")]
        public float speed = 1f;

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

        [Header("攻擊判定")]
        [Min(0f)]
        [Tooltip("整招傷害（錢袋重時會再乘 Boss 攻擊倍率）。命中區間沒填傷害時平分。")]
        public float damage = 900f;

        [Tooltip("每一格 = 一下攻擊，動畫播到 開始~結束（比例）之間有判定，每一下最多打中主角一次。")]
        public List<GreedHitWindow> hitWindows = new List<GreedHitWindow>();

        [Header("收招")]
        [Min(0f)]
        [Tooltip("這招播完（沒有派生）後回待機，待機多久再選下一招。")]
        public float recoveryIdle = 1f;

        [Header("音效 / 配音 / 特效")]
        public GreedFx fx = new GreedFx();

        [Header("派生（由上往下檢查，第一個成立的就接）")]
        public List<GreedMoveBranch> branches = new List<GreedMoveBranch>();

        public bool IsValid => clip != null && !string.IsNullOrEmpty(id);
        public float Duration => clip != null ? clip.length / Mathf.Max(0.1f, speed) : 0f;
    }

    /// <summary>
    /// 一個動作的音效 / 配音 / 特效（格式同主角的動作清單）。全部留空 = 什麼都不播，不會報錯。
    /// 時間點是「這段動畫的比例」，依動畫實際播放進度觸發（停格時也會正確等待）。
    /// </summary>
    [Serializable]
    public class GreedFx
    {
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
        public AnimationClip flyClip;
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
        public AnimationClip kickClip;
        [Min(0.1f)] public float kickSpeedMultiplier = 1f;

        [Range(0f, 1f)]
        [Tooltip("踢中天秤的幀：播到這裡時天秤開始播 Break（翻倒）並開啟天秤傷害碰撞體。")]
        public float kickImpactRatio = 0.55f;

        [Header("墜落：俯衝 (Dive) → 落地 (Fall)")]
        public AnimationClip diveClip;
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

        public AnimationClip fallClip;
        [Min(0.1f)] public float fallSpeedMultiplier = 1f;

        [Header("落地衝擊")]
        [Min(0f)] public float impactDamage = 1500f;
        [Min(0f)] public float impactRadius = 3f;

        [Header("音效 / 配音 / 特效（時間點 = 該段動畫的比例）")]
        [Tooltip("起飛（Fly）：例如振翅聲、起飛嘶吼。")]
        public GreedFx flyFx = new GreedFx();
        [Tooltip("踢翻（Kick）：例如踢擊聲、喊聲（時間點可對到 kickImpactRatio）。")]
        public GreedFx kickFx = new GreedFx();
        [Tooltip("俯衝（Dive）：例如破風聲、俯衝嘶吼。")]
        public GreedFx diveFx = new GreedFx();
        [Tooltip("落地 + 趴地爬起（Fall）：時間點 0 = 撞地那一刻，適合放撞擊聲、煙塵 / 震波特效。")]
        public GreedFx fallFx = new GreedFx();

        [Header("保險")]
        [Min(1f)]
        [Tooltip("任何一段飛行超過這麼多秒還沒到定位就強制瞬移到位（避免卡死）。")]
        public float maxTravelSeconds = 15f;

        public float FlyDuration => flyClip ? flyClip.length / flySpeedMultiplier : 0f;
        public float KickDuration => kickClip ? kickClip.length / kickSpeedMultiplier : 0f;
        public float DiveDuration => diveClip ? diveClip.length / diveSpeedMultiplier : 0f;
        public float FallDuration => fallClip ? fallClip.length / fallSpeedMultiplier : 0f;
    }
}
