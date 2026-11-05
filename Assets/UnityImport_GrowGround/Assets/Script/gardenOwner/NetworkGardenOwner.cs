using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkPlayerController), typeof(GardenPlayer))]
public sealed class NetworkGardenOwner : NetworkBehaviour
{
    [Header("Fusion prefabs")]
    [SerializeField] private NetworkObject trapPrefab;
    [SerializeField] private NetworkObject dogPrefab;
    [SerializeField] private NetworkObject slipperPrefab;
    [SerializeField] private AStarGrid navigationGrid;
    [SerializeField] private Transform throwPoint;
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField, Min(0f)] private float placementDistance = 1.5f;
    [SerializeField, Min(0f)] private float trapCooldown = 5f;
    [SerializeField, Min(0f)] private float dogCooldown = 15f;
    [SerializeField, Min(0f)] private float slipperCooldown = 1f;
    [SerializeField, Min(0f)] private float throwSpeed = 18f;
    [SerializeField] private float throwUpSpeed = 3f;

    [Networked] private NetworkObject ActiveDog { get; set; }
    private NetworkPlayerController playerController;
    private GardenPlayer gardenPlayer;
    private float nextTrapTime;
    private float nextDogTime;
    private float nextSlipperTime;

    private void Awake()
    {
        playerController = GetComponent<NetworkPlayerController>();
        gardenPlayer = GetComponent<GardenPlayer>();
    }

    public override void Spawned()
    {
        if (navigationGrid == null)
            navigationGrid = FindFirstObjectByType<AStarGrid>();
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority || playerController.CurrentRole != NetworkPlayerTeam.OrchardOwner ||
            !gardenPlayer.CanMove || !GetInput(out NetworkPlayerInput input))
            return;

        NetworkButtons previous = default;
        NetworkButtons pressed = input.Buttons.GetPressed(previous);
        if (pressed.IsSet(PlayerInputButton.GardenTrap))
            TrySpawnTrap();
        if (pressed.IsSet(PlayerInputButton.GardenDog))
            TrySpawnDog();
        if (pressed.IsSet(PlayerInputButton.GardenSlipper))
            TrySpawnSlipper();
    }

    private void TrySpawnTrap()
    {
        if (trapPrefab == null || Time.time < nextTrapTime) return;
        Vector3 origin = transform.position + transform.forward * placementDistance + Vector3.up * 2f;
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 5f, groundMask,
            QueryTriggerInteraction.Ignore)) return;

        NetworkObject spawned = Runner.Spawn(trapPrefab, hit.point + hit.normal * 0.03f,
            Quaternion.FromToRotation(Vector3.up, hit.normal) * transform.rotation);
        GardenTrap trap = spawned.GetComponent<GardenTrap>();
        if (trap != null) trap.Initialize(Object);
        nextTrapTime = Time.time + trapCooldown;
    }

    private void TrySpawnDog()
    {
        if (dogPrefab == null || ActiveDog != null || Time.time < nextDogTime ||
            navigationGrid == null || !navigationGrid.isActiveAndEnabled) return;
        Vector3 requested = transform.position + transform.forward * placementDistance;
        if (!navigationGrid.TryGetWalkablePosition(requested, 2f, out Vector3 spawn)) return;

        ActiveDog = Runner.Spawn(dogPrefab, spawn + Vector3.up * 0.05f, transform.rotation);
        GardenDog dog = ActiveDog.GetComponent<GardenDog>();
        if (dog != null) dog.Initialize(gardenPlayer, navigationGrid);
        nextDogTime = Time.time + dogCooldown;
    }

    private void TrySpawnSlipper()
    {
        if (slipperPrefab == null || Time.time < nextSlipperTime) return;
        Vector3 origin = throwPoint != null ? throwPoint.position : transform.position + Vector3.up * 1.3f;
        NetworkObject spawned = Runner.Spawn(slipperPrefab, origin, transform.rotation);
        SlipperProjectile slipper = spawned.GetComponent<SlipperProjectile>();
        if (slipper != null)
            slipper.Initialize(Object, transform.forward * throwSpeed + Vector3.up * throwUpSpeed);
        nextSlipperTime = Time.time + slipperCooldown;
    }
}
