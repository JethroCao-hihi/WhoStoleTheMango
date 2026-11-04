using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController), typeof(GardenPlayer))]
public class gardenOwner : MonoBehaviour
{
    [Header("Movement")]
    public bool readKeyboardInput = true;
    public Transform movementCamera;
    [Min(0f)] public float walkSpeed = 3f;
    [Min(0f)] public float runSpeed = 6f;
    [Min(0f)] public float jumpHeight = 1.5f;
    public float gravity = -25f;
    [Min(0f)] public float rotationSpeed = 12f;

    [Header("Animation: Bool parameters")]
    public Animator animator;
    public bool isJumping { get; private set; }
    public bool isWalk { get; private set; }
    public bool isRun { get; private set; }

    [Header("Skills: Q / E / R")]
    public GardenTrap trapPrefab;
    public GardenDog dogPrefab;
    public SlipperProjectile slipperPrefab;
    public AStarGrid navigationGrid;
    public Transform throwPoint;
    public LayerMask groundMask = ~0;
    [Min(0f)] public float placementDistance = 1.5f;
    [Min(0f)] public float trapCooldown = 5f;
    [Min(0f)] public float dogCooldown = 15f;
    [Min(0f)] public float slipperCooldown = 1f;
    [Min(0f)] public float throwSpeed = 18f;
    public float throwUpSpeed = 3f;

    public float TrapCooldownRemaining => Mathf.Max(0f, nextTrapTime - Time.time);
    public float DogCooldownRemaining => Mathf.Max(0f, nextDogTime - Time.time);
    public float SlipperCooldownRemaining => Mathf.Max(0f, nextSlipperTime - Time.time);

    private CharacterController controller;
    private GardenPlayer player;
    private GardenDog activeDog;
    private float verticalSpeed;
    private float nextTrapTime;
    private float nextDogTime;
    private float nextSlipperTime;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        controller.minMoveDistance = 0f;
        player = GetComponent<GardenPlayer>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator != null) animator.applyRootMotion = false;
        if (movementCamera == null && Camera.main != null) movementCamera = Camera.main.transform;
    }

    private void Update()
    {
        // External controllers can call Move and the three Try... methods instead.
        if (!readKeyboardInput) return;
        Keyboard keyboard = Keyboard.current;
        Vector2 input = Vector2.zero;
        bool run = false;
        bool jump = false;
        if (keyboard != null)
        {
            input.x = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f)
                - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
            input.y = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f)
                - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
            run = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            jump = keyboard.spaceKey.wasPressedThisFrame;
        }

        Move(input, run, jump);
        if (keyboard == null || !player.CanMove) return;
        if (keyboard.qKey.wasPressedThisFrame) TryPlaceTrap();
        if (keyboard.eKey.wasPressedThisFrame) TryCallDog();
        if (keyboard.rKey.wasPressedThisFrame) TryThrowSlipper();
    }

    public void Move(Vector2 input, bool run, bool jump)
    {
        if (!controller.enabled) return;
        float deltaTime = Time.deltaTime;
        Vector3 forward = movementCamera != null ? movementCamera.forward : Vector3.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        input = Vector2.ClampMagnitude(input, 1f);
        Vector3 direction = player.CanMove ? forward * input.y + right * input.x : Vector3.zero;

        bool grounded = controller.isGrounded;
        if (grounded && verticalSpeed < 0f) verticalSpeed = -2f;
        float effectiveGravity = Mathf.Min(-0.01f, gravity);
        if (grounded && jump && player.CanMove)
            verticalSpeed = Mathf.Sqrt(jumpHeight * -2f * effectiveGravity);
        verticalSpeed += effectiveGravity * deltaTime;

        if (direction.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction),
                1f - Mathf.Exp(-rotationSpeed * deltaTime));

        CollisionFlags flags = controller.Move((direction * (run ? runSpeed : walkSpeed)
            + Vector3.up * verticalSpeed) * deltaTime);
        if ((flags & CollisionFlags.Above) != 0 && verticalSpeed > 0f) verticalSpeed = 0f;
        isJumping = !controller.isGrounded;
        Vector3 horizontalVelocity = controller.velocity;
        horizontalVelocity.y = 0f;
        bool moving = horizontalVelocity.sqrMagnitude > 0.01f;
        isWalk = moving && !run && !isJumping;
        isRun = moving && run && !isJumping;
        UpdateAnimator();
    }

    private void UpdateAnimator()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        animator.SetBool("isJumping", isJumping);
        animator.SetBool("isWalk", isWalk);
        animator.SetBool("isRun", isRun);
    }

    public bool TryPlaceTrap()
    {
        if (!player.CanMove || trapPrefab == null || Time.time < nextTrapTime) return false;
        Vector3 origin = transform.position + transform.forward * placementDistance + Vector3.up * 2f;
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 5f, groundMask,
            QueryTriggerInteraction.Ignore)) return false;
        GardenTrap trap = Instantiate(trapPrefab, hit.point + hit.normal * 0.03f,
            Quaternion.FromToRotation(Vector3.up, hit.normal) * transform.rotation);
        trap.Initialize(player);
        nextTrapTime = Time.time + trapCooldown;
        return true;
    }

    public bool TryCallDog()
    {
        if (!player.CanMove || dogPrefab == null || activeDog != null || Time.time < nextDogTime)
            return false;
        if (navigationGrid == null) navigationGrid = FindFirstObjectByType<AStarGrid>();
        if (navigationGrid == null || !navigationGrid.isActiveAndEnabled) return false;
        Vector3 requestedPosition = transform.position + transform.forward * placementDistance;
        if (!navigationGrid.TryGetWalkablePosition(requestedPosition, 2f, out Vector3 spawn)) return false;
        activeDog = Instantiate(dogPrefab, spawn + Vector3.up * 0.05f, transform.rotation);
        activeDog.Initialize(player, navigationGrid);
        nextDogTime = Time.time + dogCooldown;
        return true;
    }

    public bool TryThrowSlipper()
    {
        if (!player.CanMove || slipperPrefab == null || Time.time < nextSlipperTime) return false;
        Vector3 origin = throwPoint != null ? throwPoint.position : transform.position + Vector3.up * 1.3f;
        SlipperProjectile slipper = Instantiate(slipperPrefab, origin, transform.rotation);
        slipper.Initialize(player, transform.forward * throwSpeed + Vector3.up * throwUpSpeed);
        nextSlipperTime = Time.time + slipperCooldown;
        return true;
    }

    private void OnDisable()
    {
        isJumping = isWalk = isRun = false;
        verticalSpeed = 0f;
        UpdateAnimator();
    }
}
