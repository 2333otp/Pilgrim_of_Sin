using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class Player : Character
{
    [Header("Move")]
    public float walkSpeed = 5f;
    public float runSpeed = 9f;
    public float jumpForce = 14f;
    public float rollSpeed = 10f;

    [Header("Ground Check")]
    public LayerMask groundLayer;
    public float groundCheckDistance = 0.05f;

    [Header("Combat")]
    public int currentWeaponIndex = 0;
    public int weaponCount = 2;
    public float attackDuration = 0.4f;
    public float specialAttackDuration = 0.6f;
    public float rollDuration = 0.35f;
    public float weaponSwitchDuration = 0.3f;

    public Rigidbody Rb { get; private set; }
    public Animator Anim { get; private set; }
    public Collider Col { get; private set; }
    public StateMachine StateMachine { get; private set; }

    // 移動與地面
    public Vector2 MoveInput { get; private set; }
    public bool IsGrounded { get; private set; }

    // 按鍵狀態（每幀由 ReadInput 更新，狀態腳本只讀這些）
    public bool JumpPressed { get; private set; }
    public bool RollPressed { get; private set; }
    public bool SprintHeld { get; private set; }
    public bool LightAttackPressed { get; private set; }
    public bool HeavyAttackPressed { get; private set; }
    public bool SpecialAttackPressed { get; private set; }
    public bool WeaponSwitchPressed { get; private set; }

    // States
    public PlayerIdle IdleState { get; private set; }
    public PlayerWalk WalkState { get; private set; }
    public PlayerRun RunState { get; private set; }
    public PlayerJump JumpState { get; private set; }
    public PlayerFall FallState { get; private set; }
    public PlayerGround GroundState { get; private set; }
    public PlayerRoll RollState { get; private set; }
    public PlayerAttack AttackState { get; private set; }
    public PlayerSpecialAttack SpecialAttackState { get; private set; }
    public PlayerWeaponSwitch WeaponSwitchState { get; private set; }
    public PlayerDead DeadState { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        Rb = GetComponent<Rigidbody>();
        Col = GetComponent<Collider>();
        TryGetComponent(out Animator anim);
        Anim = anim;

        StateMachine = new StateMachine();
        IdleState = new PlayerIdle(this, StateMachine);
        WalkState = new PlayerWalk(this, StateMachine);
        RunState = new PlayerRun(this, StateMachine);
        JumpState = new PlayerJump(this, StateMachine);
        FallState = new PlayerFall(this, StateMachine);
        GroundState = new PlayerGround(this, StateMachine);
        RollState = new PlayerRoll(this, StateMachine);
        AttackState = new PlayerAttack(this, StateMachine);
        SpecialAttackState = new PlayerSpecialAttack(this, StateMachine);
        WeaponSwitchState = new PlayerWeaponSwitch(this, StateMachine);
        DeadState = new PlayerDead(this, StateMachine);
    }

    private void Start()
    {
        StateMachine.Initialize(IdleState);
    }

    private void Update()
    {
        ReadInput();
        CheckGrounded();
        StateMachine.Update();
        HandleRotation();
    }

    private void FixedUpdate()
    {
        StateMachine.FixedUpdate();
    }

    private void ReadInput()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        float h = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f)
                - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
        float v = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f)
                - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
        MoveInput = new Vector2(h, v);

        JumpPressed = kb.spaceKey.wasPressedThisFrame;
        RollPressed = kb.leftShiftKey.wasPressedThisFrame;
        SprintHeld = kb.leftShiftKey.isPressed;
        WeaponSwitchPressed = kb.qKey.wasPressedThisFrame;

        var mouse = Mouse.current;
        // 設計文件：輕攻J/左鍵，重攻I/右鍵，特殊招式O/中鍵
        LightAttackPressed = kb.jKey.wasPressedThisFrame
                          || (mouse != null && mouse.leftButton.wasPressedThisFrame);
        HeavyAttackPressed = kb.iKey.wasPressedThisFrame
                          || (mouse != null && mouse.rightButton.wasPressedThisFrame);
        SpecialAttackPressed = kb.oKey.wasPressedThisFrame
                            || (mouse != null && mouse.middleButton.wasPressedThisFrame);
    }

    private void CheckGrounded()
    {
        Bounds bounds = Col.bounds;
        Vector3 origin = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        IsGrounded = Physics.CheckBox(
            origin,
            new Vector3(bounds.size.x * 0.45f, groundCheckDistance * 0.5f, bounds.size.z * 0.45f),
            Quaternion.identity,
            groundLayer);
    }

    private void HandleRotation()
    {
        Vector3 dir = new Vector3(MoveInput.x, 0f, MoveInput.y);
        if (dir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(dir);
    }

    public void PlayAnim(string clipName)
    {
        Anim?.Play(clipName);
    }

    public void SetVelocityXZ(float x, float z)
    {
        Rb.linearVelocity = new Vector3(x, Rb.linearVelocity.y, z);
    }

    public void SetVelocityY(float y)
    {
        Rb.linearVelocity = new Vector3(Rb.linearVelocity.x, y, Rb.linearVelocity.z);
    }

    public void SetVelocity(float x, float y, float z)
    {
        Rb.linearVelocity = new Vector3(x, y, z);
    }

    public override void TakeDamage(float amount)
    {
        base.TakeDamage(amount);
        if (IsDead)
            StateMachine.ChangeState(DeadState);
    }

    private void OnDrawGizmosSelected()
    {
        if (Col == null) return;
        Bounds bounds = Col.bounds;
        Gizmos.color = IsGrounded ? Color.green : Color.red;
        Vector3 origin = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        Gizmos.DrawWireCube(origin, new Vector3(bounds.size.x * 0.9f, groundCheckDistance, bounds.size.z * 0.9f));
    }
}
