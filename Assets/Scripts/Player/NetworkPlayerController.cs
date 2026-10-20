using Fusion;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class NetworkPlayerController : NetworkBehaviour
{
    [Header("Movement - Thief GDD")]
    [SerializeField, Min(0f)] private float walkSpeed = 3.5f;
    [SerializeField, Min(0f)] private float runSpeed = 6f;
    [SerializeField, Min(0f)] private float mediumLoadRunSpeed = 5.4f;
    [SerializeField, Min(0f)] private float fullLoadRunSpeed = 4.8f;
    [SerializeField, Min(0f)] private float rotationSpeed = 12f;
    [SerializeField] private float gravity = -20f;

    [Header("Stamina - Initial balance values")]
    [SerializeField, Min(1f)] private float maxStamina = 100f;
    [SerializeField, Min(0f)] private float staminaDrainPerSecond = 20f;
    [SerializeField, Min(0f)] private float staminaRecoveryPerSecond = 15f;
    [SerializeField, Min(0f)] private float minimumStaminaToStartRun = 10f;

    [Header("Climbing")]
    [SerializeField] private float climbSpeed = 3f;
    [SerializeField] private float climbInteractRadius = 2f;
    [SerializeField] private float climbArrivalThreshold = 0.05f;

    [Header("Harvesting")]
    [SerializeField] private float harvestDuration = 2.5f;
    [SerializeField] private float harvestRadius = 3f;

    [Networked]
    private NetworkButtons PreviousButtons { get; set; }
    [Networked]
    private NetworkBool IsClimbing { get; set; }

    [Networked]
    private NetworkBool IsOnTree { get; set; }

    [Networked]
    private NetworkBool IsClimbingDown { get; set; }

    [Networked]
    private Vector3 ClimbTarget { get; set; }

    [Networked]
    private Vector3 ClimbBasePosition { get; set; }

    [Networked]
    public NetworkBool IsHarvesting { get; private set; }

    [Networked]
    public float HarvestProgress { get; private set; }

    [Networked]
    public float Stamina { get; private set; }

    [Networked]
    public NetworkBool IsSprinting { get; private set; }

    private CharacterController characterController;
    private float verticalVelocity;
    private NetworkMango currentMangoTarget;
    private float harvestElapsed;

    public bool IsClimbingForHUD => IsClimbing;
    public bool IsOnTreeForHUD => IsOnTree;
    public bool IsHarvestingForHUD => IsHarvesting;
    public bool IsSprintingForHUD => IsSprinting;
    public float MaxStaminaForHUD => maxStamina;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
    }

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            Stamina = maxStamina;
            IsSprinting = false;
        }

        ApplyCharacterControllerState();

        Debug.Log(
            $"[Player] Spawned: {name} | " +
            $"StateAuthority={Object.HasStateAuthority}",
            this
        );
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority)
            return;

        bool hasInput = GetInput(out NetworkPlayerInput input);
        bool interactPressed = false;
        bool harvestPressed = false;

        // E/F la thao tac nhan; Shift la trang thai giu nut.
        if (hasInput)
        {
            NetworkButtons pressedButtons =
                input.Buttons.GetPressed(PreviousButtons);

            interactPressed =
                pressedButtons.IsSet(PlayerInputButton.Interact);

            harvestPressed =
                pressedButtons.IsSet(PlayerInputButton.Harvest);

            PreviousButtons = input.Buttons;
        }

        bool hasMoveInput = hasInput && input.Move.sqrMagnitude > 0.001f;
        bool sprintRequested = hasMoveInput &&
            input.Buttons.IsSet(PlayerInputButton.Sprint) &&
            !IsClimbing && !IsOnTree && !IsHarvesting;

        // Host/StateAuthority tinh the luc va trang thai chay cho ca mang.
        UpdateStamina(sprintRequested);

        // Leo cay tiep tuc chay ke ca tick hien tai khong co input.
        if (IsClimbing)
        {
            ProcessClimbing();
            return;
        }

        // Tien trinh hai tiep tuc chay va nhan vat dung yen trong luc hai.
        if (IsHarvesting)
        {
            ProcessHarvest();
            return;
        }

        if (!hasInput)
            return;

        // E: leo len cay hoac leo xuong.
        if (interactPressed)
        {
            HandleClimbInteraction();

            if (IsClimbing || IsOnTree)
            {
                IsSprinting = false;
                ApplyCharacterControllerState();
                return;
            }
        }

        // F: bat dau hai khi dang tren cay.
        if (harvestPressed && IsOnTree)
        {
            TryStartHarvest();

            if (IsHarvesting)
            {
                IsSprinting = false;
                return;
            }
        }

        // Khong di chuyen tu do khi dang an tren cay.
        if (IsOnTree)
            return;

        // WASD di chuyen theo huong camera.
        Quaternion cameraRotation =
            Quaternion.Euler(0f, input.CameraYaw, 0f);

        Vector3 moveDirection = cameraRotation * new Vector3(
            input.Move.x,
            0f,
            input.Move.y
        );

        moveDirection = Vector3.ClampMagnitude(moveDirection, 1f);

        if (moveDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation =
                Quaternion.LookRotation(moveDirection, Vector3.up);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationSpeed * Runner.DeltaTime
            );
        }

        if (characterController.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        float currentMoveSpeed = IsSprinting ? GetRunSpeedByMangoLoad() : walkSpeed;

        Vector3 velocity =
            moveDirection * currentMoveSpeed +
            Vector3.up * verticalVelocity;

        characterController.Move(velocity * Runner.DeltaTime);
    }

    private void UpdateStamina(bool sprintRequested)
    {
        bool canContinueOrStartSprint = sprintRequested &&
            (IsSprinting || Stamina >= minimumStaminaToStartRun);

        if (canContinueOrStartSprint)
        {
            Stamina = Mathf.Max(
                0f,
                Stamina - staminaDrainPerSecond * Runner.DeltaTime
            );

            IsSprinting = Stamina > 0.001f;
        }
        else
        {
            IsSprinting = false;
            Stamina = Mathf.Min(
                maxStamina,
                Stamina + staminaRecoveryPerSecond * Runner.DeltaTime
            );
        }
    }

    private float GetRunSpeedByMangoLoad()
    {
        NetworkPlayerData playerData = GetComponent<NetworkPlayerData>();
        int mangoCount = playerData != null ? playerData.MangoCount : 0;

        if (mangoCount >= NetworkPlayerData.MaxMangoCount)
            return fullLoadRunSpeed;

        if (mangoCount >= 3)
            return mediumLoadRunSpeed;

        return runSpeed;
    }

    private void HandleClimbInteraction()
    {
        // Nếu đã ở trên cây, E sẽ bắt đầu leo xuống.
        if (IsOnTree)
        {
            IsClimbingDown = true;
            IsClimbing = true;
            ClimbTarget = ClimbBasePosition;

            characterController.enabled = false;

            Debug.Log("[Climb] Bắt đầu leo xuống.", this);
            return;
        }

        MangoTree nearestTree = FindNearestClimbableTree();

        if (nearestTree == null)
        {
            Debug.Log(
                "[Climb] Không có cây hợp lệ trong phạm vi tương tác.",
                this
            );
            return;
        }

        ClimbBasePosition = nearestTree.ClimbStartPoint.position;
        ClimbTarget = nearestTree.ClimbEndPoint.position;

        IsClimbingDown = false;
        IsClimbing = true;
        IsOnTree = false;

        verticalVelocity = 0f;
        characterController.enabled = false;

        Debug.Log(
            $"[Climb] Bắt đầu leo cây: {nearestTree.name} | " +
            $"Target={ClimbTarget}",
            this
        );
    }

    private MangoTree FindNearestClimbableTree()
    {
        MangoTree[] trees =
            FindObjectsByType<MangoTree>(FindObjectsSortMode.None);

        MangoTree nearestTree = null;
        float nearestDistanceSquared =
            climbInteractRadius * climbInteractRadius;

        foreach (MangoTree tree in trees)
        {
            if (tree == null || !tree.HasClimbPoints)
                continue;

            Vector3 difference =
                transform.position - tree.ClimbStartPoint.position;

            // Khoảng cách ngang; tránh khác biệt độ cao làm lệch kiểm tra.
            difference.y = 0f;

            float distanceSquared = difference.sqrMagnitude;

            if (distanceSquared <= nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearestTree = tree;
            }
        }

        return nearestTree;
    }

    private void ProcessClimbing()
    {
        if (Runner == null)
            return;

        transform.position = Vector3.MoveTowards(
            transform.position,
            ClimbTarget,
            climbSpeed * Runner.DeltaTime
        );

        if (Vector3.Distance(transform.position, ClimbTarget)
            > climbArrivalThreshold)
        {
            return;
        }

        transform.position = ClimbTarget;

        IsClimbing = false;

        if (IsClimbingDown)
        {
            IsOnTree = false;
            IsClimbingDown = false;

            verticalVelocity = -2f;

            Debug.Log("[Climb] Đã xuống đất.", this);
        }
        else
        {
            IsOnTree = true;

            Debug.Log("[Climb] Đã lên vị trí ẩn nấp.", this);
        }

        ApplyCharacterControllerState();
    }

    public override void Render()
    {
        // Mỗi peer điều chỉnh collider cục bộ theo trạng thái mạng.
        ApplyCharacterControllerState();
    }

    private void ApplyCharacterControllerState()
    {
        if (characterController == null)
            return;

        bool shouldBeEnabled = !IsClimbing && !IsOnTree;

        if (characterController.enabled != shouldBeEnabled)
            characterController.enabled = shouldBeEnabled;
    }
    private bool TryStartHarvest()
    {
        NetworkPlayerData playerData = GetComponent<NetworkPlayerData>();

        if (playerData == null)
        {
            Debug.LogError(
                "[Harvest] Player prefab thiếu NetworkPlayerData.",
                this
            );
            return false;
        }

        if (playerData.MangoCount >= NetworkPlayerData.MaxMangoCount)
        {
            Debug.Log("[Harvest] Túi xoài đã đầy.", this);
            return false;
        }

        NetworkMango target = FindNearestHarvestableMango();

        if (target == null)
        {
            Debug.Log("[Harvest] Không có xoài trong tầm hái.", this);
            return false;
        }

        currentMangoTarget = target;
        harvestElapsed = 0f;
        HarvestProgress = 0f;
        IsHarvesting = true;

        Debug.Log(
            $"[Harvest] Bắt đầu hái {target.name}.",
            this
        );

        return true;
    }

    private NetworkMango FindNearestHarvestableMango()
    {
        NetworkMango[] mangoes =
            FindObjectsByType<NetworkMango>(FindObjectsSortMode.None);

        NetworkMango nearest = null;

        float nearestDistanceSquared = harvestRadius * harvestRadius;

        foreach (NetworkMango mango in mangoes)
        {
            if (mango == null || !mango.CanBeCollected)
                continue;

            float distanceSquared =
                (mango.transform.position - transform.position).sqrMagnitude;

            if (distanceSquared <= nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearest = mango;
            }
        }

        return nearest;
    }

    private void ProcessHarvest()
    {
        if (currentMangoTarget == null ||
            !currentMangoTarget.CanBeCollected)
        {
            CancelHarvest();
            return;
        }

        harvestElapsed += Runner.DeltaTime;

        HarvestProgress = Mathf.Clamp01(
            harvestElapsed / Mathf.Max(0.01f, harvestDuration)
        );

        if (HarvestProgress < 1f)
            return;

        NetworkPlayerData playerData =
            GetComponent<NetworkPlayerData>();

        bool success =
            playerData != null &&
            currentMangoTarget.TryCollect(playerData);

        Debug.Log(
            success
                ? "[Harvest] Hái xoài thành công."
                : "[Harvest] Không thể thu hoạch xoài.",
            this
        );

        CancelHarvest();
    }

    private void CancelHarvest()
    {
        IsHarvesting = false;
        HarvestProgress = 0f;
        harvestElapsed = 0f;
        currentMangoTarget = null;
    }
}