public class PlayerWalk : PlayerState
{
    public PlayerWalk(Player player, StateMachine stateMachine) : base(player, stateMachine) { }

    public override void Enter()
    {
        player.PlayAnim("Walk");
    }

    public override void Update()
    {
        if (player.IsDead) return;

        if (player.JumpPressed && player.IsGrounded)
        { stateMachine.ChangeState(player.JumpState); return; }

        if (player.RollPressed && player.IsGrounded)
        { stateMachine.ChangeState(player.RollState); return; }

        if (player.SpecialAttackPressed)
        { stateMachine.ChangeState(player.SpecialAttackState); return; }

        if (player.LightAttackPressed || player.HeavyAttackPressed)
        { stateMachine.ChangeState(player.AttackState); return; }

        if (!player.IsGrounded)
        { stateMachine.ChangeState(player.FallState); return; }

        if (player.MoveInput.sqrMagnitude < 0.01f)
        { stateMachine.ChangeState(player.IdleState); return; }

        if (player.SprintHeld)
            stateMachine.ChangeState(player.RunState);
    }

    public override void FixedUpdate()
    {
        player.SetVelocityXZ(player.MoveInput.x * player.walkSpeed, player.MoveInput.y * player.walkSpeed);
    }

    public override void Exit() { }
}
