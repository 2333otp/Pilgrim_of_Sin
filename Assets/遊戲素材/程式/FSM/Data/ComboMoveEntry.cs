using System;
using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 出招表裡的一招（無雙式連段研究：ResearchWeaponCombo）。
    /// 放哪段動畫就播哪段，動作長度一律 = 動畫長度 ÷ 播放速度，不用另外設定，
    /// 之後動畫改長度或換動作也不會被提早切掉或卡住。
    /// </summary>
    [Serializable]
    public class ComboMoveEntry
    {
        [Tooltip("這一招要播的動畫（可以放 animation/Sliced 裡切好的短動畫，也可以放任何 Humanoid 動畫）。留空 = 這一招不存在。")]
        public AnimationClip clip;

        [Min(0.1f)]
        [Tooltip("播放速度。1 = 原速；1.2 = 快 20%（動作時間也跟著變短）。")]
        public float speed = 1f;

        [Range(0f, 1f)]
        [Tooltip("接收開始：動畫播到這個比例之後按的鍵才算數（之前按的不算），而且只認這之後的第一個按鍵（⬜ / 🔺 / 翻滾）。")]
        public float inputStartRatio = 0.4f;

        [Range(0f, 1f)]
        [Tooltip("取消時間點：最早能出下一招的時間。已收到的指令等到這裡才出；過了這裡才按就立刻出。設 1 = 這一招播完才接。")]
        public float cancelRatio = 0.5f;

        [Min(0f)]
        [Tooltip("這一招的傷害。0 = 沿用主角 PlayerCombat 上的舊數值。\n有命中區間時，這是整招的總傷害（區間沒填傷害就平分）。")]
        public float damage;

        [Tooltip("命中區間：每一格 = 這一招裡的一下揮擊，動畫播到 開始~結束（比例）之間才有攻擊判定，\n" +
                 "同一個敵人每一下最多被打一次。\n" +
                 "留空 = 整招只算一下（舊行為）。\n" +
                 "可以用選單 PilgrimOfSin → 主角 → 出招表 → 自動抓命中區間，依手部揮動速度自動產生。")]
        public List<HitWindow> hitWindows = new List<HitWindow>();

        [Tooltip("勾選 = 這一招用自己的判定形狀；不勾 = 用武器預設。（每一下也可以再各自設定）")]
        public bool customHitbox;
        public HitboxShape hitbox = new HitboxShape();

        [Tooltip("打中時的打擊感檔位（鏡頭晃動／頓幀／手把震動／閃白）。\n" +
                 "自動 = N1~N3 輕、N4 中、C1~C3 中、C4 重、停頓連打 中、蓄力 重。\n" +
                 "有多個命中區間時，自動只套在最後一下，前面幾下用「輕」。")]
        public HitImpactLevel impact = HitImpactLevel.Auto;

        [Header("音效 / 配音 / 特效（用法同動作清單）")]
        [Tooltip("音效音檔，可放多個，每次隨機播一個。")]
        public AudioClip[] sfx = new AudioClip[0];
        [Range(0f, 1f)] public float sfxVolume = 1f;
        [Range(0f, 1f)]
        [Tooltip("音效播放時間點（動畫長度的比例）。")]
        public float sfxNormalizedTime;

        public AudioClip[] voiceEnglish = new AudioClip[0];
        public AudioClip[] voiceTaiwanese = new AudioClip[0];
        [Range(0f, 1f)] public float voiceVolume = 1f;
        [Range(0f, 1f)] public float voiceNormalizedTime;

        public PlayerVfxEntry[] vfx = new PlayerVfxEntry[0];

        public bool IsValid => clip != null;

        /// <summary>實際動作秒數 = 動畫長度 ÷ 播放速度。</summary>
        public float Duration => clip != null ? clip.length / Mathf.Max(0.1f, speed) : 0f;

        public AudioClip[] GetVoice(VoiceLanguage language)
            => language == VoiceLanguage.English ? voiceEnglish : voiceTaiwanese;

        public bool HasHitWindows => hitWindows != null && hitWindows.Count > 0;

        /// <summary>第 i 下的傷害：區間有填就用區間的；沒填就把整招傷害平分給沒填的區間。</summary>
        public float GetWindowDamage(int i, float moveDamage) => HitWindow.DamageAt(hitWindows, i, moveDamage);

        /// <summary>動畫播到 progress 時落在第幾個命中區間（-1 = 沒有）。</summary>
        public int GetWindowAt(float progress) => HitWindow.IndexAt(hitWindows, progress);
    }

    /// <summary>出招表一招裡的一下揮擊（時間用動畫長度的比例）。</summary>
    [Serializable]
    public class HitWindow
    {
        [Range(0f, 1f)]
        [Tooltip("攻擊判定開始（動畫長度的比例）。")]
        public float start;

        [Range(0f, 1f)]
        [Tooltip("攻擊判定結束（動畫長度的比例）。")]
        public float end = 1f;

        [Min(0f)]
        [Tooltip("這一下的傷害。0 = 整招傷害扣掉有填的區間後，由沒填的區間依「比重」分配。")]
        public float damage;

        [Min(0f)]
        [Tooltip("比重：傷害沒填時，整招傷害照比重分給各下。0 或 1 = 平均。\n例：兩下填 1、2 → 第二下拿 2/3。")]
        public float weight = 1f;

        private float EffectiveWeight => weight > 0f ? weight : 1f;

        [Tooltip("勾選 = 這一下用自己的判定形狀（例如投擲那一下）；不勾 = 用這一招／武器的設定。")]
        public bool customHitbox;
        public HitboxShape hitbox = new HitboxShape();

        [Tooltip("這一下的打擊感檔位。自動 = 最後一下用整招的檔位，前面幾下用「輕」。")]
        public HitImpactLevel impact = HitImpactLevel.Auto;

        /// <summary>
        /// 第 i 下實際的打擊感檔位：這一下有指定就用；否則最後一下 = 整招檔位，前面幾下 = 輕（整招是「無」就都無）。
        /// </summary>
        public static HitImpactLevel ImpactAt(List<HitWindow> windows, int i, HitImpactLevel moveLevel)
        {
            if (windows != null && i >= 0 && i < windows.Count && windows[i].impact != HitImpactLevel.Auto)
                return windows[i].impact;
            if (windows == null || i >= windows.Count - 1 || moveLevel == HitImpactLevel.None) return moveLevel;
            return HitImpactLevel.Light;
        }

        /// <summary>動畫播到 progress 時落在第幾個區間（-1 = 沒有）。</summary>
        public static int IndexAt(List<HitWindow> windows, float progress)
        {
            if (windows == null) return -1;
            for (int i = 0; i < windows.Count; i++)
                if (progress >= windows[i].start && progress < windows[i].end) return i;
            return -1;
        }

        /// <summary>第 i 下的傷害：有填就用填的；沒填就把整招傷害扣掉有填的之後，照比重分給沒填的。</summary>
        public static float DamageAt(List<HitWindow> windows, int i, float totalDamage)
        {
            if (windows == null || i < 0 || i >= windows.Count) return totalDamage;
            if (windows[i].damage > 0f) return windows[i].damage;
            float fixedSum = 0f, weightSum = 0f;
            foreach (var w in windows)
            {
                if (w.damage > 0f) fixedSum += w.damage;
                else weightSum += w.EffectiveWeight;
            }
            if (weightSum <= 0f) return 0f;
            return Mathf.Max(0f, totalDamage - fixedSum) * windows[i].EffectiveWeight / weightSum;
        }
    }

    /// <summary>
    /// 一把武器的出招表：
    ///   一般攻擊 ⬜ 連按：N1 → N2 → N3 → N4
    ///   強攻擊 🔺：直接按 = C1；第 1~3 下一般攻擊後按 = C2~C4
    /// </summary>
    [Serializable]
    public class ComboMoveSet
    {
        [Tooltip("勾選 = 這把武器改用下面的出招表。不勾 = 維持舊連段（輕重／重重／重輕／輕輕輕重）。")]
        public bool enabled;

        [Header("一般攻擊 ⬜（連按）")]
        public ComboMoveEntry n1 = new ComboMoveEntry();
        public ComboMoveEntry n2 = new ComboMoveEntry();
        public ComboMoveEntry n3 = new ComboMoveEntry();
        public ComboMoveEntry n4 = new ComboMoveEntry();

        [Header("強攻擊 🔺（C1 直接按；C2~C4 = 第 1~3 下一般攻擊後按）")]
        public ComboMoveEntry c1 = new ComboMoveEntry();
        public ComboMoveEntry c2 = new ComboMoveEntry();
        public ComboMoveEntry c3 = new ComboMoveEntry();
        public ComboMoveEntry c4 = new ComboMoveEntry();

        [Header("惡魔獵人分支（沒放動畫 = 這把武器沒有這招，照一般連段走）")]
        [Tooltip("停頓連打：⬜ ⬜（停半拍）⬜")]
        public ComboMoveEntry pauseCombo = new ComboMoveEntry();

        [Range(1, 3)]
        [Tooltip("停頓連打接在第幾下一般攻擊之後。2 = ⬜ ⬜（停）⬜。")]
        public int pauseAfterNormal = 2;

        [Min(0f)]
        [Tooltip("停頓寬限（秒）：那一下一般攻擊播完後，再等這麼久還可以按 ⬜ 出停頓連打；超過就收招。\n" +
                 "停頓區間 = 那一下的取消時間點 → 播完 + 寬限。取消時間點之前按 ⬜ 是一般連段的下一下。")]
        public float pauseGraceSeconds = 0.3f;

        [Tooltip("蓄力：🔺 長按")]
        public ComboMoveEntry chargeHold = new ComboMoveEntry();

        [Min(0.05f)]
        [Tooltip("🔺 按住超過這麼多秒算蓄力；更早放開就出原本的強攻擊（C1~C4）。\n" +
                 "只有放了蓄力動畫的武器才會等這段時間，沒放的按 🔺 立刻出招。")]
        public float chargeHoldSeconds = 0.25f;

        [Header("秘劍 R1（不受上面「使用出招表」勾選影響）")]
        [Tooltip("秘劍的命中區間：每一格 = 一下，各算一次傷害（時間是秘劍動畫長度的比例）。\n" +
                 "留空 = 整招只算一下（舊行為：一開始就開判定，整招每個敵人只打一次）。\n" +
                 "傷害總量 = 主角 PlayerCombat 上的特殊技數值，區間沒填傷害就平分。")]
        public List<HitWindow> specialHitWindows = new List<HitWindow>();

        [Tooltip("秘劍的打擊感檔位。自動 = 重（多段時最後一下重、前面幾下輕）。")]
        public HitImpactLevel specialImpact = HitImpactLevel.Auto;

        [Tooltip("勾選 = 秘劍用自己的判定形狀；不勾 = 用武器預設。")]
        public bool specialCustomHitbox;
        public HitboxShape specialHitbox = new HitboxShape();

        [Header("攻擊判定形狀（不受上面「使用出招表」勾選影響）")]
        [Tooltip("勾選 = 這把武器的出招表招式與秘劍用下面的預設形狀。不勾 = 沿用主角 Prefab 上 AttackHitBox 的球形判定（舊行為）。")]
        public bool useWeaponHitbox;
        public HitboxShape weaponHitbox = new HitboxShape();

        public const int Count = 4;

        /// <summary>
        /// 決定某一下要用的判定形狀：這一下自訂 → 這一招自訂 → 武器預設 → null（舊的球形判定）。
        /// </summary>
        public HitboxShape ResolveHitbox(HitWindow window, bool moveCustom, HitboxShape moveShape)
        {
            if (window != null && window.customHitbox && window.hitbox != null) return window.hitbox;
            if (moveCustom && moveShape != null) return moveShape;
            if (useWeaponHitbox && weaponHitbox != null) return weaponHitbox;
            return null;
        }

        /// <summary>勾選且至少有 N1 才使用。</summary>
        public bool IsUsable => enabled && n1 != null && n1.IsValid;

        /// <summary>一般攻擊第 index 下（0 = N1）。</summary>
        public ComboMoveEntry GetNormal(int index) => index switch
        {
            0 => n1, 1 => n2, 2 => n3, 3 => n4, _ => null,
        };

        /// <summary>強攻擊（0 = C1 直接按；k = 第 k 下一般攻擊後按的 C(k+1)）。</summary>
        public ComboMoveEntry GetCharge(int index) => index switch
        {
            0 => c1, 1 => c2, 2 => c3, 3 => c4, _ => null,
        };
    }
}
