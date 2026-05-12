using UnityEngine;

public class PlayerJump : PlayerState
{
    public PlayerJump(Player player, StateMachine stateMachine) : base(player, stateMachine) { }

    public override void Enter()
    {
        player.PlayAnim("Jump");
        player.SetVelocityY(player.jumpForce);
    }

    public override void Update()
    {
        if (player.IsDead) return;

        if (player.Rb.linearVelocity.y < 0f)
            stateMachine.ChangeState(player.FallState);
    }

    public override void FixedUpdate()
    {
        player.SetVelocityXZ(player.MoveInput.x * player.walkSpeed, player.MoveInput.y * player.walkSpeed);
    }

    public override void Exit() { }
}
