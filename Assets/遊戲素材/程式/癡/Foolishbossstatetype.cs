namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// Boss「癡」AI 行為狀態。
    /// 場地機制（善惡區切換）由 FoolishBossController 獨立管理。
    /// </summary>
    public enum FoolishBossStateType
    {
        Idle,       // 短暫待機，決策下一步
        Move,       // 移動接近玩家
        Attack1,    // 攻擊1（傷害800）
        Attack2,    // 攻擊2（傷害1000）
        Attack3,    // 攻擊3（傷害1700）
        Stagger,    // 受擊硬直（選配）
        Dead,       // HP=0，觸發通關
    }

    /// <summary>
    /// 場地機制相位（粗分：給癡的 AI 判斷用）。
    /// </summary>
    public enum FoolishPhase
    {
        Evade,      // 躲避階段（善惡區、緩衝、嘶吼、擴散）：癡預設無敵
        Battle,     // 打鬥窗口：10秒，Boss 可受傷，可攻擊
    }

    /// <summary>
    /// 場地循環的細部階段。一個大循環：
    /// ZoneShow → Buffer → ZoneShow → Buffer → Battle → Roar → Spread → 回到 ZoneShow
    /// </summary>
    public enum FoolishCyclePhase
    {
        ZoneShow,   // 善/惡區出現：惡區生效（善區內不扣血）
        Buffer,     // 正常地板緩衝：沒有善惡區
        Battle,     // 打鬥窗口：沒有善惡區，癡可被打
        Roar,       // 癡嘶吼（惡區即將重新出現的信號）
        Spread,     // 地面從邊緣擴散變黑（還不扣血）
    }
}