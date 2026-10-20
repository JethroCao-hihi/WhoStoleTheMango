using System.Collections.Generic;
using Fusion;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class NetworkPlayerController : NetworkBehaviour
{
    [Header("Thief movement - GDD")]
    [SerializeField, Min(0f)] private float walkSpeed = 3.5f;
    [SerializeField, Min(0f)] private float runSpeed = 6f;
    [SerializeField, Min(0f)] private float mediumLoadRunSpeed = 5.4f; // Provisional balance value
    [SerializeField, Min(0f)] private float fullLoadRunSpeed = 4.8f;
    [SerializeField, Min(0f)] private float rotationSpeed = 12f;
    [SerializeField] private float gravity = -20f;

    [Header("Stamina - values to tune after playtest")]
    [SerializeField, Min(1f)] private float maxStamina = 100f;
    [SerializeField, Min(0f)] private float staminaDrainPerSecond = 20f; // Provisional; GDD does not specify rate
    [SerializeField, Min(0f)] private float staminaRecoveryPerSecond = 15f; // Provisional; GDD does not specify rate
    [SerializeField, Min(0f)] private float minimumStaminaToStartRun = 10f; // Provisional anti-flicker threshold

    [Header("Long jump over ditches - GDD")]
    [SerializeField, Min(0f)] private float jumpVerticalSpeed = 7.5f;
    [SerializeField, Min(0f)] private float jumpHorizontalSpeed = 6f;
    [SerializeField, Min(0f)] private float jumpStaminaCost = 25f;
    [SerializeField, Min(0f)] private float jumpCooldown = 5f;

    [Header("Crouch / hide - GDD")]
    [SerializeField, Range(0.2f, 1f)] private float crouchSpeedMultiplier = 0.5f;
    [SerializeField, Min(0.5f)] private float crouchedHeight = 1.2f;

    [Header("Water - tune in playtest")]
    [SerializeField, Range(0.1f, 1f)] private float waterMoveMultiplier = 0.45f;

    [Header("Rescue teammates - GDD")]
    [SerializeField, Min(0.1f)] private float rescueRadius = 2f; // Prototype radius; GDD does not specify rescue range
    [SerializeField, Min(0.1f)] private float rescueDuration = 5f;
    [SerializeField, Min(0f)] private float rescueProtectionDuration = 3f;

    [Header("Climbing")]
    [SerializeField] private float climbSpeed = 3f;
    [SerializeField] private float climbInteractRadius = 2f;
    [SerializeField] private float climbArrivalThreshold = 0.05f;

    [Header("Harvesting - GDD")]
    [SerializeField] private float harvestDuration = 2.5f;
    [SerializeField] private float harvestRadius = 3f;

    [Header("Dropped mangoes")]
    [SerializeField] private NetworkObject droppedMangoPrefab;
    [SerializeField, Min(0.2f)] private float droppedMangoPickupRadius = 1.6f;
    [SerializeField, Min(0f)] private float droppedMangoForwardOffset = 0.8f;

    [Header("Footstep / sound detection")]
    [SerializeField, Min(0.05f)] private float walkFootstepInterval = 0.7f;
    [SerializeField, Min(0.05f)] private float runFootstepInterval = 0.45f;
    [SerializeField, Min(0.05f)] private float crouchFootstepInterval = 1f;
    [SerializeField, Min(0f)] private float walkNoiseRadius = 5f;
    [SerializeField, Min(0f)] private float crouchNoiseRadius = 2f;
    [SerializeField, Min(0f)] private float runNoiseRadius = 10f;
    [SerializeField, Min(0f)] private float noisySurfaceRunRadius = 15f;
    [SerializeField, Min(0f)] private float harvestNoiseRadius = 10f;
    [SerializeField, Min(0f)] private float splashNoiseRadius = 20f;
    [SerializeField] private LayerMask noisySurfaceLayers;

    [Networked] private NetworkButtons PreviousButtons { get; set; }
    [Networked] private NetworkBool IsClimbing { get; set; }
    [Networked] private NetworkBool IsOnTree { get; set; }
    [Networked] private NetworkBool IsClimbingDown { get; set; }
    [Networked] private Vector3 ClimbTarget { get; set; }
    [Networked] private Vector3 ClimbBasePosition { get; set; }

    [Networked] public NetworkBool IsHarvesting { get; private set; }
    [Networked] public float HarvestProgress { get; private set; }
    [Networked] public float Stamina { get; private set; }
    [Networked] public NetworkBool IsSprinting { get; private set; }
    [Networked] public NetworkBool IsJumping { get; private set; }
    [Networked] public float JumpCooldownRemaining { get; private set; }
    [Networked] public NetworkBool IsCrouching { get; private set; }
    [Networked] public NetworkBool IsInWater { get; private set; }
    [Networked] public NetworkBool IsHiddenInCover { get; private set; }
    [Networked] public NetworkBool IsCaptured { get; private set; }
    [Networked] public NetworkBool IsRescuing { get; private set; }
    [Networked] public float InteractionProgress { get; private set; }
    [Networked] public float RescueProtectionRemaining { get; private set; }

    // Buffers Q if it is pressed while the thief is climbing, jumping, or harvesting.
    [Networked] private NetworkBool PendingDropMangoRequest { get; set; }

    [Networked] private Vector3 JumpDirection { get; set; }

    private CharacterController characterController;
    private NetworkPlayerData playerData;
    private NetworkPlayerSoundState soundState;
    private float verticalVelocity;
    private float harvestElapsed;
    private float actionElapsed;
    private float footstepElapsed;
    private NetworkMango currentMangoTarget;
    private NetworkPlayerController currentActionTarget;
    private bool actionRequiresRelease;
    private float standingHeight;
    private Vector3 standingCenter;
    private bool hasStandingColliderValues;
    private readonly HashSet<ThiefCoverZone> activeCoverZones = new HashSet<ThiefCoverZone>();

    public bool IsClimbingForHUD => IsClimbing;
    public bool IsOnTreeForHUD => IsOnTree;
    public bool IsHarvestingForHUD => IsHarvesting;
    public bool IsSprintingForHUD => IsSprinting;
    public float MaxStaminaForHUD => maxStamina;
    // This branch implements the thief character only. Role selection/owner logic belongs to another feature branch.
    public bool IsThief => true;
    public bool IsOwner => false; // Compatibility for older UI/scripts; this thief-only branch has no owner actions.

    private void Awake()
    {
        CacheComponents();
        CacheStandingColliderValues();
    }

    private void CacheComponents()
    {
        if (characterController == null)
            characterController = GetComponent<CharacterController>();
        if (playerData == null)
            playerData = GetComponent<NetworkPlayerData>();
        if (soundState == null)
            soundState = GetComponent<NetworkPlayerSoundState>();
    }

    private void CacheStandingColliderValues()
    {
        if (characterController == null || hasStandingColliderValues)
            return;

        standingHeight = characterController.height;
        standingCenter = characterController.center;
        hasStandingColliderValues = true;
    }

    public override void Spawned()
    {
        CacheComponents();
        CacheStandingColliderValues();

        if (Object.HasStateAuthority)
        {
            Stamina = GetMaxStamina();
            IsSprinting = false;
            IsCaptured = false;
            RescueProtectionRemaining = 0f;
            IsRescuing = false;
            InteractionProgress = 0f;
        }

        ApplyCharacterControllerState();
        Debug.Log($"[Player] Spawned: {name} | StateAuthority={Object.HasStateAuthority}", this);
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority)
            return;

        CacheComponents();
        float dt = Runner.DeltaTime;
        bool hasInput = GetInput(out NetworkPlayerInput input);
        NetworkButtons pressedButtons = default;

        if (hasInput)
        {
            pressedButtons = input.Buttons.GetPressed(PreviousButtons);
            PreviousButtons = input.Buttons;
        }

        bool interactPressed = pressedButtons.IsSet(PlayerInputButton.Interact);
        bool harvestPressed = pressedButtons.IsSet(PlayerInputButton.Harvest);
        bool jumpPressed = pressedButtons.IsSet(PlayerInputButton.Jump);
        bool dropPressed = pressedButtons.IsSet(PlayerInputButton.DropMango);
        if (dropPressed)
        {
            PendingDropMangoRequest = true;
            CacheComponents();
            Debug.Log(
                $"[Mango Drop Debug] Q received by StateAuthority. " +
                $"OnTree={IsOnTree} | Climbing={IsClimbing} | Harvesting={IsHarvesting} | " +
                $"Inventory={(playerData != null ? playerData.MangoCount : -1)}.", this);
        }
#if UNITY_EDITOR
        bool debugGiveMangoPressed = pressedButtons.IsSet(PlayerInputButton.DebugGiveMango);
#endif
        bool sprintHeld = hasInput && input.Buttons.IsSet(PlayerInputButton.Sprint);
        bool crouchHeld = hasInput && input.Buttons.IsSet(PlayerInputButton.Crouch);
        bool rescueHeld = hasInput && input.Buttons.IsSet(PlayerInputButton.Rescue);

        if (JumpCooldownRemaining > 0f)
            JumpCooldownRemaining = Mathf.Max(0f, JumpCooldownRemaining - dt);
        if (RescueProtectionRemaining > 0f)
            RescueProtectionRemaining = Mathf.Max(0f, RescueProtectionRemaining - dt);

        IsHiddenInCover = IsCrouching && activeCoverZones.Count > 0;

        if (IsCaptured)
        {
            // Do not carry a queued drop through capture and unexpectedly drop after release.
            PendingDropMangoRequest = false;
            UpdateStamina(false);
            IsSprinting = false;
            IsCrouching = false;
            IsRescuing = false;
            InteractionProgress = 0f;
            CancelHarvest();
            ApplyCharacterControllerState();
            return;
        }

        // R held: a thief rescues a teammate whose captured state is set by the shared capture feature later.
        if (ProcessRescue(rescueHeld, dt))
        {
            UpdateStamina(false);
            IsSprinting = false;
            IsCrouching = false;
            UpdateCoverState();
            ApplyCharacterControllerState();
            return;
        }

        bool hasMoveInput = hasInput && input.Move.sqrMagnitude > 0.001f;
        bool crouchRequested = crouchHeld && !IsJumping && !IsOnTree && !IsHarvesting && !IsInWater;
        bool wasCrouching = IsCrouching;
        IsCrouching = crouchRequested;
        if (wasCrouching != IsCrouching)
            Debug.Log($"[Thief Input] Crouch={(IsCrouching ? "ON" : "OFF")} | Input authority snapshot received.", this);

        bool sprintRequested = hasMoveInput && sprintHeld && !IsCrouching &&
            !IsClimbing && !IsOnTree && !IsHarvesting && !IsInWater && !IsJumping;
        // While airborne, don't recover stamina just because the player isn't running.
        if (!IsJumping && !jumpPressed)
            UpdateStamina(sprintRequested);
        else
            IsSprinting = false;

        if (IsJumping)
        {
            ProcessLongJump(dt);
            UpdateCoverState();
            return;
        }

        if (IsClimbing)
        {
            IsSprinting = false;
            ProcessClimbing();
            UpdateCoverState();
            return;
        }

        if (IsHarvesting)
        {
            IsSprinting = false;
            IsCrouching = false;
            ProcessHarvest();
            UpdateCoverState();
            return;
        }

        // Process Q before requiring a fresh input snapshot. This also lets a queued Q
        // execute after climbing/jumping/harvesting has finished. Dropping is allowed while on the tree.
        if (PendingDropMangoRequest && !IsClimbing && !IsHarvesting && !IsJumping && !IsCaptured)
        {
            PendingDropMangoRequest = false;
            Debug.Log(
                $"[Mango Drop Debug] Processing queued Q. OnTree={IsOnTree} | " +
                $"Inventory={(playerData != null ? playerData.MangoCount : -1)}.", this);
            DropOneMango();
        }

        if (!hasInput)
        {
            ProcessGravityOnly(dt);
            UpdateCoverState();
            return;
        }

        Vector3 moveDirection = GetCameraRelativeMove(input.Move, input.CameraYaw);

        if (interactPressed)
        {
            // E picks up a dropped mango first, unless the player is on a tree (E climbs down).
            if (!IsOnTree && TryPickupDroppedMango())
            {
                UpdateCoverState();
                return;
            }

            HandleClimbInteraction();
            if (IsClimbing || IsOnTree)
            {
                IsSprinting = false;
                IsCrouching = false;
                ApplyCharacterControllerState();
                return;
            }
        }

        if (harvestPressed && IsOnTree)
        {
            TryStartHarvest();
            if (IsHarvesting)
            {
                IsSprinting = false;
                IsCrouching = false;
                return;
            }
        }

#if UNITY_EDITOR
        if (debugGiveMangoPressed)
            DebugGrantOneMango();
#endif

        if (jumpPressed)
        {
            Debug.Log($"[Thief Input] Jump received | grounded={characterController.isGrounded} | stamina={Stamina:F1} | cooldown={JumpCooldownRemaining:F1}", this);
            if (!IsCrouching && !IsInWater && !IsOnTree && !IsClimbing && !IsHarvesting)
                TryStartLongJump(moveDirection);
        }

        if (IsOnTree)
        {
            UpdateCoverState();
            return;
        }

        if (moveDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * dt);
        }

        if (characterController.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        float speed = GetCurrentMoveSpeed();
        if (IsCrouching)
            speed *= crouchSpeedMultiplier;
        if (IsInWater)
            speed *= waterMoveMultiplier;

        verticalVelocity += gravity * dt;
        Vector3 velocity = moveDirection * speed + Vector3.up * verticalVelocity;
        CollisionFlags flags = characterController.Move(velocity * dt);

        if ((flags & CollisionFlags.Below) != 0 && verticalVelocity < 0f)
            verticalVelocity = -2f;

        UpdateFootstepNoise(moveDirection, dt);
        UpdateCoverState();
    }

    private Vector3 GetCameraRelativeMove(Vector2 move, float cameraYaw)
    {
        Quaternion cameraRotation = Quaternion.Euler(0f, cameraYaw, 0f);
        Vector3 direction = cameraRotation * new Vector3(move.x, 0f, move.y);
        return Vector3.ClampMagnitude(direction, 1f);
    }

    private float GetMaxStamina() => maxStamina;

    private float GetCurrentMoveSpeed()
    {
        return IsSprinting ? GetRunSpeedByMangoLoad() : walkSpeed;
    }

    private void UpdateStamina(bool sprintRequested)
    {
        float staminaMax = GetMaxStamina();
        bool canSprint = sprintRequested &&
            (IsSprinting || Stamina >= minimumStaminaToStartRun);

        if (canSprint)
        {
            Stamina = Mathf.Max(0f, Stamina - staminaDrainPerSecond * Runner.DeltaTime);
            IsSprinting = Stamina > 0.001f;
        }
        else
        {
            IsSprinting = false;
            Stamina = Mathf.Min(staminaMax, Stamina + staminaRecoveryPerSecond * Runner.DeltaTime);
        }
    }

    private float GetRunSpeedByMangoLoad()
    {
        CacheComponents();
        int mangoCount = playerData != null ? playerData.MangoCount : 0;
        if (mangoCount >= NetworkPlayerData.MaxMangoCount)
            return fullLoadRunSpeed;
        if (mangoCount >= 3)
            return mediumLoadRunSpeed;
        return runSpeed;
    }

    private void TryStartLongJump(Vector3 moveDirection)
    {
        if (JumpCooldownRemaining > 0f)
        {
            Debug.Log($"[Thief Jump] Cooldown còn lại: {JumpCooldownRemaining:F1}s.", this);
            return;
        }
        if (Stamina < jumpStaminaCost)
        {
            Debug.Log($"[Thief Jump] Không đủ thể lực: cần {jumpStaminaCost:F1}, hiện có {Stamina:F1}.", this);
            return;
        }
        if (!characterController.enabled || !characterController.isGrounded)
        {
            Debug.Log("[Thief Jump] Bạn phải đứng trên mặt đất để nhảy.", this);
            return;
        }

        if (moveDirection.sqrMagnitude <= 0.001f)
            moveDirection = transform.forward;

        JumpDirection = Vector3.ProjectOnPlane(moveDirection, Vector3.up).normalized;
        if (JumpDirection.sqrMagnitude < 0.001f)
            JumpDirection = transform.forward;

        Stamina = Mathf.Max(0f, Stamina - jumpStaminaCost);
        JumpCooldownRemaining = jumpCooldown;
        verticalVelocity = jumpVerticalSpeed;
        IsJumping = true;
        IsSprinting = false;

        Debug.Log($"[Thief Jump] Started | horizontal={jumpHorizontalSpeed:F1} | vertical={jumpVerticalSpeed:F1} | stamina={Stamina:F1}", this);
        EmitNoise(PlayerNoiseType.Jump, runNoiseRadius);
    }

    private void ProcessLongJump(float dt)
    {
        if (characterController == null || !characterController.enabled)
            return;

        verticalVelocity += gravity * dt;
        float horizontalSpeed = Mathf.Min(jumpHorizontalSpeed, GetCurrentJumpHorizontalSpeed());
        Vector3 velocity = JumpDirection * horizontalSpeed + Vector3.up * verticalVelocity;
        CollisionFlags flags = characterController.Move(velocity * dt);

        if (verticalVelocity <= 0f && (((flags & CollisionFlags.Below) != 0) || characterController.isGrounded))
        {
            IsJumping = false;
            verticalVelocity = -2f;
            if (!IsInWater)
                EmitNoise(PlayerNoiseType.Landing, walkNoiseRadius);
        }
    }

    private float GetCurrentJumpHorizontalSpeed() => GetRunSpeedByMangoLoad();

    private void ProcessGravityOnly(float dt)
    {
        if (characterController == null || !characterController.enabled || IsOnTree || IsClimbing)
            return;

        if (characterController.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;
        verticalVelocity += gravity * dt;
        characterController.Move(Vector3.up * verticalVelocity * dt);
    }

    /// <summary>
    /// Thief-side rescue only. The Orchard Owner capture action is intentionally handled by another branch.
    /// R must be held for rescueDuration near a teammate whose IsCaptured state was set by the owner-side system.
    /// </summary>
    private bool ProcessRescue(bool isHeld, float dt)
    {
        if (!isHeld)
        {
            currentActionTarget = null;
            actionElapsed = 0f;
            actionRequiresRelease = false;
            IsRescuing = false;
            InteractionProgress = 0f;
            return false;
        }

        if (actionRequiresRelease)
            return false;

        if (!IsRescuing || currentActionTarget == null || !IsValidRescueTarget(currentActionTarget))
        {
            currentActionTarget = FindNearestCapturedTeammate(rescueRadius);
            actionElapsed = 0f;
        }

        if (currentActionTarget == null)
        {
            IsRescuing = false;
            InteractionProgress = 0f;
            return false;
        }

        if (Vector3.Distance(transform.position, currentActionTarget.transform.position) > rescueRadius)
        {
            currentActionTarget = null;
            actionElapsed = 0f;
            IsRescuing = false;
            InteractionProgress = 0f;
            return false;
        }

        IsRescuing = true;
        actionElapsed += dt;
        InteractionProgress = Mathf.Clamp01(actionElapsed / rescueDuration);

        if (actionElapsed >= rescueDuration)
        {
            currentActionTarget.ReleaseFromCapture();
            currentActionTarget = null;
            actionElapsed = 0f;
            actionRequiresRelease = true;
            IsRescuing = false;
            InteractionProgress = 0f;
        }

        return true;
    }

    private bool IsValidRescueTarget(NetworkPlayerController target)
    {
        return target != null && target != this && target.IsCaptured;
    }

    private NetworkPlayerController FindNearestCapturedTeammate(float radius)
    {
        NetworkPlayerController[] players = FindObjectsByType<NetworkPlayerController>(FindObjectsSortMode.None);
        NetworkPlayerController nearest = null;
        float nearestSqr = radius * radius;

        foreach (NetworkPlayerController candidate in players)
        {
            if (candidate == null || candidate == this || !candidate.IsCaptured)
                continue;

            float distanceSqr = (candidate.transform.position - transform.position).sqrMagnitude;
            if (distanceSqr <= nearestSqr)
            {
                nearestSqr = distanceSqr;
                nearest = candidate;
            }
        }

        return nearest;
    }

    // Integration hook for the owner/capture feature branch. This thief-only branch does not call it itself.
    public void ApplyCapturedState()
    {
        if (Object == null || !Object.HasStateAuthority || IsCaptured)
            return;

        IsCaptured = true;
        IsJumping = false;
        IsCrouching = false;
        IsSprinting = false;
        IsClimbing = false;
        IsOnTree = false;
        IsClimbingDown = false;
        IsRescuing = false;
        IsInWater = false;
        IsHiddenInCover = false;
        CancelHarvest();
        DropAllMangoes();
        EmitNoise(PlayerNoiseType.Capture, splashNoiseRadius);
        ApplyCharacterControllerState();
        Debug.Log($"[Capture] {name} bị bắt.", this);
    }

    private void ReleaseFromCapture()
    {
        if (Object == null || !Object.HasStateAuthority || !IsCaptured)
            return;

        IsCaptured = false;
        RescueProtectionRemaining = rescueProtectionDuration;
        IsRescuing = false;
        verticalVelocity = -2f;
        ApplyCharacterControllerState();
        Debug.Log($"[Rescue] {name} đã được đồng đội giải cứu.", this);
    }

    private void DropAllMangoes()
    {
        CacheComponents();
        if (playerData == null)
            return;

        int count = Mathf.Clamp(playerData.MangoCount, 0, NetworkPlayerData.MaxMangoCount);
        if (count > 0 && droppedMangoPrefab == null)
        {
            Debug.LogError("[Mango Drop] Chưa gán Dropped Mango Prefab; không thể làm rơi số xoài đang mang.", this);
            return;
        }

        for (int i = 0; i < count; i++)
        {
            Vector2 spread = Random.insideUnitCircle * 0.45f;
            Vector3 dropPosition = transform.position + Vector3.up * 0.25f + new Vector3(spread.x, 0f, spread.y);
            Vector3 landingPosition = ResolveDroppedMangoLandingPosition(dropPosition);
            if (!SpawnOneDroppedMango(dropPosition, landingPosition))
                return;
            playerData.MangoCount = Mathf.Max(0, playerData.MangoCount - 1);
        }
    }

#if UNITY_EDITOR
    private void DebugGrantOneMango()
    {
        CacheComponents();
        if (playerData == null)
        {
            Debug.LogError("[Debug Mango] Player prefab thiếu NetworkPlayerData.", this);
            return;
        }

        if (playerData.MangoCount >= NetworkPlayerData.MaxMangoCount)
        {
            Debug.Log("[Debug Mango] Túi đã đầy, không thể cấp thêm.", this);
            return;
        }

        playerData.MangoCount++;
        Debug.Log($"[Debug Mango] Đã cấp 1 quả để test Q. Inventory={playerData.MangoCount}", this);
    }
#endif

    private void DropOneMango()
    {
        CacheComponents();
        if (playerData == null)
        {
            Debug.LogError("[Mango Drop] Player prefab thiếu NetworkPlayerData.", this);
            return;
        }
        if (playerData.MangoCount <= 0)
        {
            Debug.LogWarning("[Mango Drop] Túi đang trống. Hãy hái ít nhất 1 quả trước khi nhấn Q.", this);
            return;
        }

        // Spawn near the thief, then animate the fruit to the ground in front of the tree/player.
        Vector3 landingProbe = transform.position + transform.forward * (droppedMangoForwardOffset + 0.7f);
        Vector3 landingPosition = ResolveDroppedMangoLandingPosition(landingProbe);
        Vector3 spawnPosition = transform.position + transform.forward * 0.35f + Vector3.up * 0.55f;

        if (SpawnOneDroppedMango(spawnPosition, landingPosition))
        {
            playerData.MangoCount = Mathf.Max(0, playerData.MangoCount - 1);
            Debug.Log(
                $"[Mango Drop] Đã thả 1 quả có hoạt ảnh. Start={spawnPosition} | " +
                $"Landing={landingPosition} | Còn lại={playerData.MangoCount}.", this);
        }
    }

    private bool SpawnOneDroppedMango(Vector3 spawnPosition, Vector3 landingPosition)
    {
        if (droppedMangoPrefab == null)
        {
            Debug.LogError("[Mango Drop] Chưa gán Dropped Mango Prefab vào NetworkPlayerController.", this);
            return false;
        }
        if (Runner == null || !Runner.IsRunning || Object == null || !Object.HasStateAuthority)
        {
            Debug.LogError("[Mango Drop] Cần Runner đang chạy và StateAuthority để spawn xoài.", this);
            return false;
        }

        NetworkObject dropped = Runner.Spawn(droppedMangoPrefab, spawnPosition, Quaternion.identity);
        if (dropped == null)
        {
            Debug.LogError("[Mango Drop] Fusion không spawn được Mango Prefab.", this);
            return false;
        }

        NetworkMango mango = dropped.GetComponent<NetworkMango>();
        NetworkDroppedMangoMarker droppedMarker = dropped.GetComponent<NetworkDroppedMangoMarker>();
        NetworkTransform networkTransform = dropped.GetComponent<NetworkTransform>();
        if (mango == null || droppedMarker == null)
        {
            Debug.LogError("[Mango Drop] Mango Prefab cần NetworkMango và NetworkDroppedMangoMarker trên root.", dropped);
            Runner.Despawn(dropped);
            return false;
        }
        if (networkTransform == null)
        {
            Debug.LogError("[Mango Drop] Mango Prefab thiếu NetworkTransform; hoạt ảnh không đồng bộ sang Client.", dropped);
            Runner.Despawn(dropped);
            return false;
        }

        // A mango dropped from the tree gets a little more airtime so players can see it fall.
        float duration = IsOnTree ? 0.9f : 0.55f;
        droppedMarker.MarkDropped(landingPosition, duration);
        EmitNoise(PlayerNoiseType.MangoDrop, harvestNoiseRadius);
        Debug.Log($"[Mango Drop Debug] Spawned network mango {dropped.name}; start={spawnPosition}; land={landingPosition}; duration={duration:F2}s.", dropped);
        return true;
    }

    private Vector3 ResolveDroppedMangoLandingPosition(Vector3 desiredPosition)
    {
        float rayStartY = Mathf.Max(transform.position.y + 5f, desiredPosition.y + 5f);
        Vector3 rayOrigin = new Vector3(desiredPosition.x, rayStartY, desiredPosition.z);
        RaycastHit[] hits = Physics.RaycastAll(
            rayOrigin, Vector3.down, 50f, ~0, QueryTriggerInteraction.Ignore);

        bool foundGround = false;
        float nearestDistance = float.MaxValue;
        Vector3 landingPosition = new Vector3(desiredPosition.x, 0.12f, desiredPosition.z);

        foreach (RaycastHit hit in hits)
        {
            Collider hitCollider = hit.collider;
            if (hitCollider == null || hit.normal.y < 0.35f)
                continue;
            if (hitCollider.GetComponentInParent<NetworkPlayerController>() != null)
                continue;
            if (hitCollider.GetComponentInParent<NetworkMango>() != null)
                continue;
            // Ignore trunk/canopy colliders so a fruit dropped from a tree reaches the ground.
            if (hitCollider.GetComponentInParent<MangoTree>() != null)
                continue;

            if (hit.distance < nearestDistance)
            {
                nearestDistance = hit.distance;
                landingPosition = hit.point + Vector3.up * 0.12f;
                foundGround = true;
            }
        }

        if (!foundGround)
            Debug.LogWarning(
                "[Mango Drop] Không tìm thấy collider mặt đất ở điểm rơi. " +
                "Hãy kiểm tra Ground/Terrain có Collider và nằm dưới cây.", this);

        return landingPosition;
    }

    private bool TryPickupDroppedMango()
    {
        CacheComponents();
        if (playerData == null || playerData.MangoCount >= NetworkPlayerData.MaxMangoCount)
            return false;

        NetworkMango[] mangoes = FindObjectsByType<NetworkMango>(FindObjectsSortMode.None);
        NetworkMango nearest = null;
        float nearestSqr = droppedMangoPickupRadius * droppedMangoPickupRadius;

        foreach (NetworkMango mango in mangoes)
        {
            NetworkDroppedMangoMarker droppedMarker = mango != null
                ? mango.GetComponent<NetworkDroppedMangoMarker>()
                : null;
            if (mango == null || droppedMarker == null || !droppedMarker.IsDropped ||
                droppedMarker.IsAnimatingDrop || !mango.CanBeCollected)
                continue;
            float distanceSqr = (mango.transform.position - transform.position).sqrMagnitude;
            if (distanceSqr <= nearestSqr)
            {
                nearestSqr = distanceSqr;
                nearest = mango;
            }
        }

        if (nearest == null)
            return false;

        bool success = nearest.TryCollect(playerData);
        if (success)
            Debug.Log($"[Mango Pickup] Nhặt lại xoài. Inventory={playerData.MangoCount}", this);
        return success;
    }

    private void UpdateFootstepNoise(Vector3 moveDirection, float dt)
    {
        if (soundState == null || moveDirection.sqrMagnitude <= 0.001f || IsInWater)
        {
            footstepElapsed = 0f;
            return;
        }

        float interval = IsCrouching ? crouchFootstepInterval : (IsSprinting ? runFootstepInterval : walkFootstepInterval);
        footstepElapsed += dt;
        if (footstepElapsed < interval)
            return;
        footstepElapsed = 0f;

        if (IsCrouching)
        {
            EmitNoise(PlayerNoiseType.CrouchStep, crouchNoiseRadius);
        }
        else if (IsSprinting)
        {
            float radius = IsOnNoisySurface() ? noisySurfaceRunRadius : runNoiseRadius;
            EmitNoise(PlayerNoiseType.RunStep, radius);
        }
        else
        {
            EmitNoise(PlayerNoiseType.WalkStep, walkNoiseRadius);
        }
    }

    private bool IsOnNoisySurface()
    {
        if (noisySurfaceLayers.value == 0)
            return false;

        Vector3 origin = transform.position + Vector3.up * 0.15f;
        return Physics.Raycast(origin, Vector3.down, 1.5f, noisySurfaceLayers, QueryTriggerInteraction.Ignore);
    }

    private void EmitNoise(PlayerNoiseType type, float radius)
    {
        if (soundState != null && Object != null && Object.HasStateAuthority)
            soundState.EmitNoise(type, radius);
    }

    private void UpdateCoverState()
    {
        IsHiddenInCover = IsCrouching && activeCoverZones.Count > 0;
    }

    public void EnterCoverZone(ThiefCoverZone zone)
    {
        if (Object == null || !Object.HasStateAuthority || zone == null)
            return;
        activeCoverZones.Add(zone);
        UpdateCoverState();
    }

    public void ExitCoverZone(ThiefCoverZone zone)
    {
        if (Object == null || !Object.HasStateAuthority || zone == null)
            return;
        activeCoverZones.Remove(zone);
        UpdateCoverState();
    }

    public void TryEnterWater(float waterSurfaceY)
    {
        if (Object == null || !Object.HasStateAuthority || IsInWater || characterController == null)
            return;

        // Trigger volumes should include the water body below the surface. Enter only when the feet reach it.
        float feetY = characterController.bounds.min.y;
        if (feetY > waterSurfaceY + 0.12f || verticalVelocity > 0f)
            return;

        IsInWater = true;
        IsJumping = false;
        verticalVelocity = -2f;
        EmitNoise(PlayerNoiseType.Splash, splashNoiseRadius);
        Debug.Log($"[Water] {name} entered water.", this);
    }

    public void ExitWaterZone()
    {
        if (Object == null || !Object.HasStateAuthority || !IsInWater)
            return;
        IsInWater = false;
        verticalVelocity = -2f;
        Debug.Log($"[Water] {name} left water.", this);
    }

    private void HandleClimbInteraction()
    {
        if (IsOnTree)
        {
            IsClimbingDown = true;
            IsClimbing = true;
            ClimbTarget = ClimbBasePosition;
            characterController.enabled = false;
            IsCrouching = false;
            Debug.Log("[Climb] Bắt đầu leo xuống.", this);
            return;
        }

        MangoTree nearestTree = FindNearestClimbableTree();
        if (nearestTree == null)
        {
            Debug.Log("[Climb] Không có cây hợp lệ trong phạm vi tương tác.", this);
            return;
        }

        ClimbBasePosition = nearestTree.ClimbStartPoint.position;
        ClimbTarget = nearestTree.ClimbEndPoint.position;
        IsClimbingDown = false;
        IsClimbing = true;
        IsOnTree = false;
        IsCrouching = false;
        verticalVelocity = 0f;
        characterController.enabled = false;
        EmitNoise(PlayerNoiseType.Climb, walkNoiseRadius);
        Debug.Log($"[Climb] Bắt đầu leo cây: {nearestTree.name} | Target={ClimbTarget}", this);
    }

    private MangoTree FindNearestClimbableTree()
    {
        MangoTree[] trees = FindObjectsByType<MangoTree>(FindObjectsSortMode.None);
        MangoTree nearestTree = null;
        float nearestDistanceSquared = climbInteractRadius * climbInteractRadius;

        foreach (MangoTree tree in trees)
        {
            if (tree == null || !tree.HasClimbPoints)
                continue;
            Vector3 difference = transform.position - tree.ClimbStartPoint.position;
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

        transform.position = Vector3.MoveTowards(transform.position, ClimbTarget, climbSpeed * Runner.DeltaTime);
        if (Vector3.Distance(transform.position, ClimbTarget) > climbArrivalThreshold)
            return;

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

    private bool TryStartHarvest()
    {
        CacheComponents();
        if (playerData == null)
        {
            Debug.LogError("[Harvest] Player prefab thiếu NetworkPlayerData.", this);
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
        EmitNoise(PlayerNoiseType.Harvest, harvestNoiseRadius);
        Debug.Log($"[Harvest] Bắt đầu hái {target.name}.", this);
        return true;
    }

    private NetworkMango FindNearestHarvestableMango()
    {
        NetworkMango[] mangoes = FindObjectsByType<NetworkMango>(FindObjectsSortMode.None);
        NetworkMango nearest = null;
        float nearestDistanceSquared = harvestRadius * harvestRadius;

        foreach (NetworkMango mango in mangoes)
        {
            NetworkDroppedMangoMarker droppedMarker = mango != null
                ? mango.GetComponent<NetworkDroppedMangoMarker>()
                : null;
            if (mango == null || (droppedMarker != null && droppedMarker.IsDropped) || !mango.CanBeCollected)
                continue;
            float distanceSquared = (mango.transform.position - transform.position).sqrMagnitude;
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
        NetworkDroppedMangoMarker droppedMarker = currentMangoTarget != null
            ? currentMangoTarget.GetComponent<NetworkDroppedMangoMarker>()
            : null;
        if (currentMangoTarget == null || !currentMangoTarget.CanBeCollected ||
            (droppedMarker != null && droppedMarker.IsDropped))
        {
            CancelHarvest();
            return;
        }

        harvestElapsed += Runner.DeltaTime;
        HarvestProgress = Mathf.Clamp01(harvestElapsed / Mathf.Max(0.01f, harvestDuration));
        if (HarvestProgress < 1f)
            return;

        CacheComponents();
        bool success = playerData != null && currentMangoTarget.TryCollect(playerData);
        Debug.Log(success ? "[Harvest] Hái xoài thành công." : "[Harvest] Không thể thu hoạch xoài.", this);
        CancelHarvest();
    }

    private void CancelHarvest()
    {
        IsHarvesting = false;
        HarvestProgress = 0f;
        harvestElapsed = 0f;
        currentMangoTarget = null;
    }

    public override void Render()
    {
        CacheComponents();
        ApplyCharacterControllerState();
    }

    private void ApplyCharacterControllerState()
    {
        if (characterController == null)
            return;
        CacheStandingColliderValues();

        bool shouldBeEnabled = !IsClimbing && !IsOnTree && !IsCaptured;
        if (characterController.enabled != shouldBeEnabled)
            characterController.enabled = shouldBeEnabled;

        if (hasStandingColliderValues)
        {
            float desiredHeight = IsCrouching && !IsCaptured ? Mathf.Min(crouchedHeight, standingHeight) : standingHeight;
            if (!Mathf.Approximately(characterController.height, desiredHeight))
            {
                float heightDifference = standingHeight - desiredHeight;
                characterController.height = desiredHeight;
                characterController.center = standingCenter - Vector3.up * (heightDifference * 0.5f);
            }
            else if (!IsCrouching && !Mathf.Approximately(characterController.center.y, standingCenter.y))
            {
                characterController.center = standingCenter;
            }
        }
    }
}
