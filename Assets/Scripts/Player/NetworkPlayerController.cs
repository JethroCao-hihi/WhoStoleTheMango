using Fusion;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class NetworkPlayerController : NetworkBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float rotationSpeed = 12f;
    [SerializeField] private float gravity = -20f;

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

    private CharacterController characterController;
    private float verticalVelocity;
    private NetworkMango currentMangoTarget;
    private float harvestElapsed;

    public bool IsClimbingForHUD => IsClimbing;
    public bool IsOnTreeForHUD => IsOnTree;
    public bool IsHarvestingForHUD => IsHarvesting;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
    }

    public override void Spawned()
    {
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

        // Phát hiện thao tác vừa nhấn bằng trạng thái nút của Fusion.
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

        // Luôn tiếp tục chuyển động leo dù tick hiện tại không có input.
        if (IsClimbing)
        {
            ProcessClimbing();
            return;
        }

        // Tiến trình hái tiếp tục chạy, không khởi động lại bởi input mới.
        if (IsHarvesting)
        {
            ProcessHarvest();
            return;
        }

        if (!hasInput)
            return;

        // E: chỉ leo cây hoặc leo xuống.
        if (interactPressed)
        {
            HandleClimbInteraction();

            if (IsClimbing || IsOnTree)
            {
                ApplyCharacterControllerState();
                return;
            }
        }

        // F: chỉ bắt đầu hái khi đang trên cây.
        if (harvestPressed && IsOnTree)
        {
            TryStartHarvest();

            if (IsHarvesting)
                return;
        }

        // Không chạy tự do khi đang ẩn trên cây.
        if (IsOnTree)
            return;

        // WASD di chuyển theo hướng camera.
        Quaternion cameraRotation =
            Quaternion.Euler(0f, input.CameraYaw, 0f);

        Vector3 moveDirection =
            cameraRotation * new Vector3(
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

        verticalVelocity += gravity * Runner.DeltaTime;

        Vector3 velocity =
            moveDirection * moveSpeed +
            Vector3.up * verticalVelocity;

        characterController.Move(velocity * Runner.DeltaTime);
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