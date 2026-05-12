using UnityEngine;

public class PlayerGround : PlayerState
{
    private float timer;

    public PlayerGround(Player player, StateMachine stateMachine) : base(player, stateMachine) { }

    public override void Enter()
    {
        player.PlayAnim("Land");
        player.SetVelocityXZ(0f, 0f);
        timer = 0.15f;
    }

    public override void Update()
    {
        if (player.IsDead) return;

        timer -= Time.deltaTime;
        if (timer <= 0f)
            stateMachine.ChangeState(player.MoveInput.sqrMagnitude > 0.01f ? player.WalkState : player.IdleState);
    }

    public override void FixedUpdate() { }
    public override void Exit() { }
}
