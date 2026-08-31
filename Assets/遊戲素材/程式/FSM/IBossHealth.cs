namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// Boss 血量對外介面，供通用戰鬥 HUD（CombatHUD）讀取。
    /// GreedBossController 已實作；WrathBossController / FoolishBossController 之後補上。
    /// </summary>
    public interface IBossHealth
    {
        float CurrentHp { get; }
        float MaxHp { get; }
        bool IsDead { get; }

        /// <summary>血條旁顯示的名字，例如「心魔-貪」。</summary>
        string DisplayName { get; }
    }
}
