namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 可受傷物件的共用介面。
    /// GreedBossController、WrathBossController、FoolishBossController 都實作此介面。
    /// </summary>
    public interface IDamageable
    {
        void TakeDamage(float amount);
    }
}