namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// Boss「嗔」AI 行為狀態。
    /// </summary>
    public enum WrathBossStateType
    {
        Idle,           // 短暫待機，決策下一步
        Dash,           // 沿五芒星軌跡衝刺（碰到玩家造成衝撞傷害400）
        Explode,        // 抵達定點後爆炸（範圍傷害600）
        WaitAtPainting, // 停在已被改寫的畫前（玩家攻擊窗口，固定秒數）
        Attack1,        // 攻擊1（傷害800）
        Attack2,        // 攻擊2（傷害1300）
        Attack3,        // 攻擊3（傷害1000）
        Stagger,        // 受擊硬直（選配，需開啟 enableStagger）
        Dead,           // HP=0，觸發通關
    }
}