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

    /// <summary>
    /// 可以被打斷（強制短暫硬直）的敵人，例如被主角的特殊技打中。
    /// 不受「受擊硬直」開關與冷卻限制；敵人自己決定哪些時候不能被打斷（例如空中、嘶吼）。
    /// </summary>
    public interface IInterruptible
    {
        void Interrupt();
    }
}
