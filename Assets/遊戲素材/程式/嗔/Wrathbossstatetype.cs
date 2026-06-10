namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// Boss「嗔」AI 行為狀態。
    /// </summary>
    public enum WrathBossStateType
    {
        Idle,           // 短暫待機，決策下一步
        Dash,           // 沿五芒星軌跡衝刺（碰到玩家造成衝撞傷害 400）
        StayAtVertex,   // 抵達頂點後停留 10 秒（無論畫作狀態）：未改→爆炸 600；期間隨機出招
        Attack1,        // 攻擊 1（傷害 800）
        Attack2,        // 攻擊 2（傷害 1300）
        Attack3,        // 攻擊 3（傷害 1000）
        Stagger,        // 受擊硬直（選配，需開啟 enableStagger）
        Dead,           // HP=0，觸發通關
    }
}
