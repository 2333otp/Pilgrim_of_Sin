public abstract class PlayerState : State
{
    protected Player player;
    protected StateMachine stateMachine;

    public PlayerState(Player player, StateMachine stateMachine)
    {
        this.player = player;
        this.stateMachine = stateMachine;
    }
}
