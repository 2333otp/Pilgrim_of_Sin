namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 繆爾（玩家角色）所有可能的狀態
    /// 數字代表優先級，數字越小越優先
    /// Priority 0 = 最高（Paused / Dead）
    /// Priority 1 = SpecialSkill
    /// Priority 2 = LightAttack / HeavyAttack / ComboAttack
    /// Priority 3 = Jump / Roll
    /// Priority -  = 其餘（無特殊優先級限制，隨時可進入）
    /// </summary>
    public enum PlayerStateType
    {
        // ── 待機 / 移動 ────────────────────────────────────
        Idle,
        Walk,
        Sprint,

        // ── 空中動作 ───────────────────────────────────────
        Jump,
        Fall,

        // ── 防禦性動作（有無敵幀） ─────────────────────────
        Roll,

        // ── 普通攻擊（可被敵人打斷 → Damaged） ────────────
        LightAttack,
        HeavyAttack,
        ComboAttack,          // 輕/重組合連段（4種 per 武器）

        // ── 特殊招式（不可被打斷，有無敵幀，有 CD） ────────
        SpecialSkill,
        SpecialSkillCooldown, // CD 等待，立即回 Idle 繼續行動

        // ── 武器切換 ───────────────────────────────────────
        WeaponSwitch,         // 播放換武器動畫（動畫本身有傷害判定）

        // ── 受傷 / 死亡 ───────────────────────────────────
        Damaged,              // 受傷硬直：Idle/Walk/Sprint/Jump/Fall/LightAttack/HeavyAttack/ComboAttack 可被打斷進入
                              // SpecialSkill / Roll / WeaponSwitch 不可被打斷
        Dead,                 // HP 歸零，觸發返回存檔點流程

        // ── 系統 ───────────────────────────────────────────
        Paused,               // 暫停（最高優先級 0，打斷所有動作）
    }
}
