using System;
using Godot;
using interaction;
using queen.data;
using queen.error;
using queen.events;
using queen.extension;

/// <summary>
/// The goal of this controller is to emulate the kind of controller that AAA horror games would use. Specifically targeting Resident Evil 7 (Biohazard). Whether or not is succeeds is up for debate.
/// </summary>
public partial class PsuedoAAACharController : CharacterBody3D
{
    [ExportGroup("Movement Stats")]
    [Export] private float Speed = 2.0f;
    [Export] private float SprintSpeed = 5.0f;
    [Export] private float Acceleration = 0.3f;
    [Export] private float JumpVelocity = 4.5f;
    [Export] private float mouse_sensitivity;
    [Export] private float CrouchSpeedScale = 0.45f;
    [Export] private float StepHeight = 0.4f;
    [Export] private float StepStrength = 3.0f;

    [ExportGroup("Node Paths")]
    [Export] private NodePath PathVCam;
    [Export] private NodePath PathAnimationPlayer;
    [Export] private NodePath PathCanStandCheck;
    [Export] private NodePath PathStepCheckTop;
    [Export] private NodePath PathStepCheckBottom;
    [Export] private NodePath PathInteractRay;
    [Export] private NodePath PathFlashlightLight;

    // References
    private VirtualCamera vcam;
    private AnimationPlayer anim;
    private RayCast3D CanStandCheck;
    private RayCast3D CanStepCheckTop;
    private RayCast3D CanStepCheckBottom;
    private RayCast3D InteractionRay;
    private Light3D Flashlight;


    // Values
    private Vector2 camera_look_vector = new();
    private float gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
    private float CurrentSpeed = 0.0f;
    private bool IsCrouching = false;
    private bool IsOnStairs = false;

    private float CanStepCheckTop_CastLength = 1.0f;
    private float CanStepCheckBottom_CastLength = 1.0f;
    private Vector2 InputVector = new();
    private bool LastWasInteractable = false;

    public override void _Ready()
    {
        this.GetSafe(PathVCam, out vcam);
        this.GetSafe(PathAnimationPlayer, out anim);
        this.GetSafe(PathCanStandCheck, out CanStandCheck);
        this.GetSafe(PathStepCheckTop, out CanStepCheckTop);
        this.GetSafe(PathStepCheckBottom, out CanStepCheckBottom);
        this.GetSafe(PathInteractRay, out InteractionRay);
        this.GetSafe(PathFlashlightLight, out Flashlight);

        CanStepCheckTop.Position += new Vector3(0, StepHeight, 0);
        CanStepCheckBottom_CastLength = CanStepCheckBottom.TargetPosition.Length();
        CanStepCheckTop_CastLength = CanStepCheckTop.TargetPosition.Length();

        Events.Gameplay.RequestPlayerAbleToMove += HandleEventPlayerCanMove;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }


    public override void _PhysicsProcess(double delta)
    {

        Vector3 velocity = Velocity;
        if (!IsOnFloor()) velocity.Y -= gravity * (float)delta;

        if (Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            CamLookLogic(delta);
            CamMoveLogic(ref velocity, delta);
            if (!IsCrouching) JumpLogic(ref velocity, delta);
            StepLogic(ref velocity, delta);
        }

        Velocity = velocity;
        MoveAndSlide();
    }

    private void JumpLogic(ref Vector3 velocity, double delta)
    {
        // Add the gravity.

        // Handle Jump.
        if (Input.IsActionJustPressed("ui_accept") && IsOnFloor())
            velocity.Y = JumpVelocity;
    }

    private void CamMoveLogic(ref Vector3 velocity, double delta)
    {

        InputVector = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        if (InputVector.LengthSquared() < 0.1f)
            InputVector = Input.GetVector("gamepad_move_left", "gamepad_move_right", "gamepad_move_forward", "gamepad_move_back");

        Vector3 direction = (Transform.Basis * new Vector3(InputVector.X, 0, InputVector.Y)).Normalized();
        if (direction != Vector3.Zero)
        {


            // TODO Holy fuck this is some messy logic. This should really get cleaned up somehow. Maybe by having a series of contributing factors? IDK
            // Sprint or No Sprint
            var target_speed = (IsOnFloor() && Input.IsActionPressed("sprint")) ? SprintSpeed : Speed;
            // Crouching so speed is slowed unless on stairs
            if (IsCrouching) target_speed = IsOnStairs ? Speed : (Speed * CrouchSpeedScale);
            CurrentSpeed = Mathf.Lerp(CurrentSpeed, target_speed, Acceleration);
            velocity.X = direction.X * CurrentSpeed;
            velocity.Z = direction.Z * CurrentSpeed;
        }
        else
        {
            CurrentSpeed = Mathf.Lerp(CurrentSpeed, 0, Acceleration);
            velocity.X = Mathf.MoveToward(Velocity.X, 0, CurrentSpeed);
            velocity.Z = Mathf.MoveToward(Velocity.Z, 0, CurrentSpeed);
        }
    }

    private void StepLogic(ref Vector3 velocity, double _delta)
    {
        if (InputVector.LengthSquared() < 0.8f) return;

        var dir = new Vector3(InputVector.X, 0, InputVector.Y);
        CanStepCheckBottom.TargetPosition = dir * CanStepCheckBottom_CastLength;
        CanStepCheckTop.TargetPosition = dir * CanStepCheckTop_CastLength;

        if (!IsOnWall())
        {
            IsOnStairs = false;
            return;
        }

        IsOnStairs = CanStepCheckBottom.IsColliding() && !CanStepCheckTop.IsColliding();

        if (IsOnStairs)
        {
            velocity.Y = StepHeight * StepStrength;
        }
    }

    private void CamLookLogic(double delta)
    {
        var look = (camera_look_vector.LengthSquared() > 0.1f) ? camera_look_vector : GetGamepadLookVector();
        look *= (float)delta;

        RotateY(look.X * mouse_sensitivity);

        var rot = vcam.Rotation;
        rot.X += look.Y * mouse_sensitivity;
        var cl = Mathf.DegToRad(89.0f);
        rot.X = Mathf.Clamp(rot.X, -cl, cl);
        vcam.Rotation = rot;

        camera_look_vector = Vector2.Zero;
    }

    private Vector2 GAMEPAD_VEC_FLIP = new(-1, 1);
    private Vector2 GetGamepadLookVector()
    {
        return Input.GetVector("gamepad_look_left", "gamepad_look_right", "gamepad_look_down", "gamepad_look_up")
        * Controls.Instance.ControllerLookSensitivity
        * GAMEPAD_VEC_FLIP;
    }


    private void HandleEventPlayerCanMove(bool can_move)
    {
        SetPhysicsProcess(can_move);
        // prevents random motion after returning
        Velocity = Vector3.Zero;
        camera_look_vector = Vector2.Zero;
    }
    public override void _UnhandledInput(InputEvent e)
    {
        bool handled = false;
        if (Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            handled |= InputMouseLook(e);
            handled |= InputInteract(e);
            handled |= InputCrouch(e);
            handled |= InputOpenInventory(e);
            handled |= InputToggleFlashlight(e);
            // TODO add in other controls here!

        }
        if (handled) GetViewport().SetInputAsHandled();
    }
    private bool InputMouseLook(InputEvent e)
    {
        if (e is not InputEventMouseMotion) return false;
        var mm = e as InputEventMouseMotion;
        camera_look_vector += mm.Relative * Controls.Instance.MouseLookSensivity * -1f;
        return true;
    }

    private bool InputInteract(InputEvent e)
    {

        InteractionRay.ForceRaycastUpdate();

        if (InteractionRay.GetCollider() is Node collider && collider is IInteractable inter && inter.IsActive())
        {
            if (!LastWasInteractable)
            {
                LastWasInteractable = true;
                Events.GUI.TriggerAbleToInteract(inter.GetActiveName());
            }

            if (!e.IsActionPressed("interact")) return false;
            else if (inter.Interact())
            {
                // TODO: do we want anything to happen on this end? Realistically the Interact object should handle SFX, VFX, etc...
            }
        }
        else if (LastWasInteractable)
        {
            LastWasInteractable = false;
            Events.GUI.TriggerUnableToInteract();
        }

        return true;
    }

    private bool InputCrouch(InputEvent e)
    {
        if (!e.IsActionPressed("crouch")) return false;
        if (IsCrouching && CanStandCheck.IsColliding()) return false;
        IsCrouching = !IsCrouching;
        if (IsCrouching) anim.Play("Crouch");
        else anim.PlayBackwards("Crouch");

        return true;
    }

    private bool InventoryToggle = false;
    private bool InputOpenInventory(InputEvent e)
    {
        if (!e.IsActionPressed("open_inventory")) return false;
        InventoryToggle = !InventoryToggle;
        Events.GUI.TriggerRequestInventory(InventoryToggle);
        Velocity = Vector3.Zero;
        return true;
    }

    private bool InputToggleFlashlight(InputEvent e)
    {
        if (!e.IsActionPressed("toggle_flashlight")) return false;
        Flashlight.EditorOnly = !Flashlight.EditorOnly;
        return true;
    }

}
