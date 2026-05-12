using UnityEngine;

public class PlayerFall : PlayerState
{
    public PlayerFall(Player player, StateMachine stateMachine) : base(player, stateMachine) { }

    public override void Enter()
    {
        player.PlayAnim("Fall");
    }

    public override void Update()
    {
        if (player.IsDead) return;

        if (player.IsGrounded)
            stateMachine.ChangeState(player.GroundState);
    }

    public override void FixedUpdate()
    {
        player.SetVelocityXZ(player.MoveInput.x * player.walkSpeed, player.MoveInput.y * player.walkSpeed);
    }

    public override void Exit() { }
}
