namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// Boss「貪」AI 行為狀態。
    /// 天秤相位（ScalePhase）由 GreedBossController 獨立管理，不在此枚舉。
    /// </summary>
    public enum GreedBossStateType
    {
        Idle,       // 短暫待機，決策下一個行動
        Move,       // 移動接近玩家
        Attack1,    // 傷害 900（Inspector 可調）
        Attack2,    // 傷害 1200（Inspector 可調）
        Attack3,    // 傷害 2300（Inspector 可調）
        KickScale,  // 踢翻天秤，天秤碰撞體傷害 700（Inspector 可調）
        Stagger,    // 受擊硬直（需開啟 GreedBossController.enableStagger）
        Dead,       // HP=0，觸發通關
    }

    /// <summary>
    /// 天秤相位，驅動無敵與攻擊力係數。
    /// </summary>
    public enum ScalePhase
    {
        Unbalanced, // 天秤傾斜：Boss 無敵＋攻擊力提升
        Balanced,   // 天秤平衡：Boss 可受傷，10 秒攻擊窗口
        Kicked,     // 踢翻動畫播放中，完成後回 Unbalanced
    }
}
