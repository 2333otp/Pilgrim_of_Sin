using UnityEngine;

public class PlayerRoll : PlayerState
{
    private float timer;
    private Vector3 rollDirection;

    public PlayerRoll(Player player, StateMachine stateMachine) : base(player, stateMachine) { }

    public override void Enter()
    {
        player.PlayAnim("Roll");
        timer = player.rollDuration;
        rollDirection = player.transform.forward;
    }

    public override void Update()
    {
        if (player.IsDead) return;

        timer -= Time.deltaTime;
        if (timer <= 0f)
            stateMachine.ChangeState(player.IdleState);
    }

    public override void FixedUpdate()
    {
        player.SetVelocityXZ(rollDirection.x * player.rollSpeed, rollDirection.z * player.rollSpeed);
    }

    public override void Exit()
    {
        player.SetVelocityXZ(0f, 0f);
    }
}
