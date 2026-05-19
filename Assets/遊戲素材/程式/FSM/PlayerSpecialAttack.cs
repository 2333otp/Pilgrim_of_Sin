using UnityEngine;

public class PlayerSpecialAttack : PlayerState
{
    private float timer;

    public PlayerSpecialAttack(Player player, StateMachine stateMachine) : base(player, stateMachine) { }

    public override void Enter()
    {
        player.PlayAnim("SpecialAttack");
        player.SetVelocityXZ(0f, 0f);
        timer = player.specialAttackDuration;
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
