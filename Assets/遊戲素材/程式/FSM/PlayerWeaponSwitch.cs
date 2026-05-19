using UnityEngine;

public class PlayerWeaponSwitch : PlayerState
{
    private float timer;

    public PlayerWeaponSwitch(Player player, StateMachine stateMachine) : base(player, stateMachine) { }

    public override void Enter()
    {
        player.PlayAnim("WeaponSwitch");
        player.SetVelocityXZ(0f, 0f);
        player.currentWeaponIndex = (player.currentWeaponIndex + 1) % player.weaponCount;
        timer = player.weaponSwitchDuration;
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
