public class PlayerRun : PlayerState
{
    public PlayerRun(Player player, StateMachine stateMachine) : base(player, stateMachine) { }

    public override void Enter()
    {
        player.PlayAnim("Run");
    }

    public override void Update()
    {
        if (player.IsDead) return;

        if (player.JumpPressed && player.IsGrounded)
        { stateMachine.ChangeState(player.JumpState); return; }

        if (player.RollPressed && player.IsGrounded)
        { stateMachine.ChangeState(player.RollState); return; }

        if (player.LightAttackPressed || player.HeavyAttackPressed)
        { stateMachine.ChangeState(player.AttackState); return; }

        if (!player.IsGrounded)
        { stateMachine.ChangeState(player.FallState); return; }

        if (player.MoveInput.sqrMagnitude < 0.01f)
        { stateMachine.ChangeState(player.IdleState); return; }

        if (!player.SprintHeld)
            stateMachine.ChangeState(player.WalkState);
    }

    public override void FixedUpdate()
    {
        player.SetVelocityXZ(player.MoveInput.x * player.runSpeed, player.MoveInput.y * player.runSpeed);
    }

    public override void Exit() { }
}
