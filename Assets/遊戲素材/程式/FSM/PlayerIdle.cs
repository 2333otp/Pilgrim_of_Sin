public class PlayerIdle : PlayerState
{
    public PlayerIdle(Player player, StateMachine stateMachine) : base(player, stateMachine) { }

    public override void Enter()
    {
        player.PlayAnim("Idle");
        player.SetVelocityXZ(0f, 0f);
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

        if (player.WeaponSwitchPressed)
        { stateMachine.ChangeState(player.WeaponSwitchState); return; }

        if (player.MoveInput.sqrMagnitude > 0.01f)
        {
            stateMachine.ChangeState(player.SprintHeld ? player.RunState : player.WalkState);
            return;
        }

        if (!player.IsGrounded)
            stateMachine.ChangeState(player.FallState);
    }

    public override void FixedUpdate() { }
    public override void Exit() { }
}
