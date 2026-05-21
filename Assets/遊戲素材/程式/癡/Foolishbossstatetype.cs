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
    /// 場地機制相位。
    /// </summary>
    public enum FoolishPhase
    {
        Evade,      // 躲避階段：善惡區交替，玩家須待在善區；Boss 無敵
        Battle,     // 打Boss階段：10秒，Boss 可受傷，可攻擊
    }
}