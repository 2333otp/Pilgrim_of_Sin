using UnityEngine;

public class PlayerDead : PlayerState
{
    public PlayerDead(Player player, StateMachine stateMachine) : base(player, stateMachine) { }

    public override void Enter()
    {
        player.PlayAnim("Dead");
        player.SetVelocity(0f, 0f, 0f);
        player.Rb.isKinematic = true;
    }

    public override void Update() { }
    public override void FixedUpdate() { }

    public override void Exit()
    {
        player.Rb.isKinematic = false;
    }
}
