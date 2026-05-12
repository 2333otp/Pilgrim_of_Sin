using UnityEngine;

public class PlayerAttack : PlayerState
{
    private float timer;

    public PlayerAttack(Player player, StateMachine stateMachine) : base(player, stateMachine) { }

    public override void Enter()
    {
        player.PlayAnim("Attack");
        player.SetVelocityXZ(0f, 0f);
        timer = player.attackDuration;
    }

    public override void Update()
    {
        if (player.IsDead) return;

        timer -= Time.deltaTime;
        if (timer <= 0f)
            stateMachine.ChangeState(player.IdleState);
    }

    public override void FixedUpdate() { }
    public override void Exit() { }
}
