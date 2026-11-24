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
        Action,     // 出招（招式內容與派生見 GreedBossProfile SO）
        KickScale,  // 平衡結束空襲：起飛 → 定位天秤一側 → 踢翻 → 鎖定主角墜落（見 GreedBossProfile.aerialKick）
        Stagger,    // 受擊硬直（需在素材總表勾「受擊硬直」）
        Dead,       // HP=0，觸發通關
    }

    /// <summary>
    /// 天秤相位，驅動傷害係數與 Boss 行為。
    /// </summary>
    public enum ScalePhase
    {
        StatueHeavy,   // 雕像重：雙方傷害正常，Boss 主動追擊
        Balanced,      // 天秤平衡：玩家傷害×1.5，Boss 靜止（10秒窗口）
        MoneyBagHeavy, // 錢袋重：玩家高減傷，Boss 攻擊×1.5，Boss 主動追擊
        Kicked,        // 踢翻動畫播放中，完成後回 StatueHeavy
    }
}
