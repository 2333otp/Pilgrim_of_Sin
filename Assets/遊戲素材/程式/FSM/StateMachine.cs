using UnityEngine;

public class StateMachine
{
    public State CurrentState { get; private set; }

    public void Initialize(State startState)
    {
        CurrentState = startState;
        CurrentState.Enter();
    }

    public void ChangeState(State newState)
    {
        CurrentState.Exit();
        CurrentState = newState;
        CurrentState.Enter();
    }

    public void Update()
    {
        CurrentState?.Update();
    }

    public void FixedUpdate()
    {
        CurrentState?.FixedUpdate();
    }
}
