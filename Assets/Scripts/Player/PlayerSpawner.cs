using System.Collections.Generic;
using System.Linq;
using Fusion;
using UnityEngine;

namespace MangoHeist.Networking
{
    /// <summary>
    /// Spawns one networked player per active PlayerRef. Called by PhotonLobbyManager
    /// after Fusion finishes loading GameScene and for subsequent join/leave callbacks.
    /// The active roster requirements come from PhotonLobbyManager so demo and production
    /// modes use the same authoritative spawn flow.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerSpawner : MonoBehaviour
    {
        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";

        // Five slots remain available for the official 5-player mode.
        // The 2-player Thief-only demo simply uses the first two slots.
        private static readonly Vector3[] SpawnPositions =
        {
            new(-3f, 1f, -2f),
            new(-1.5f, 1f, -2f),
            new(0f, 1f, -2f),
            new(1.5f, 1f, -2f),
            new(3f, 1f, -2f)
        };

        [SerializeField] private NetworkObject playerPrefab;

        private readonly Dictionary<PlayerRef, NetworkObject> playerObjects = new();
        private readonly Dictionary<PlayerRef, int> playerSlots = new();
        private readonly HashSet<PlayerRef> playersBeingSpawned = new();
        private NetworkRunner runner;
        private bool initialized;

        public string InitializationError { get; private set; }
        public bool IsInitialized => initialized && runner != null && runner.IsRunning;

        public bool Initialize(
            NetworkRunner source,
            IReadOnlyDictionary<PlayerRef, NetworkPlayerTeam> acceptedRoles)
        {
            if (source == null || !source.IsRunning)
                return FailInitialization("PlayerSpawner requires the active, running Lobby NetworkRunner.");

            if (playerPrefab == null || !playerPrefab.IsSpawnable)
                return FailInitialization("Player.prefab is missing or its Fusion NetworkObject is not spawnable.");

            if (source.Prefabs == null)
                return FailInitialization("The active NetworkRunner has no Fusion NetworkPrefabTable.");

            // Check the runtime table used by this Runner; a NetworkObject component or label alone is insufficient.
            NetworkPrefabId prefabId = source.Prefabs.GetId(PlayerPrefabPath);
            if (!prefabId.IsValid)
                return FailInitialization($"{PlayerPrefabPath} is not registered in the active Fusion NetworkPrefabTable.");

            if (runner != null && runner != source)
                return FailInitialization("PlayerSpawner is already bound to a different NetworkRunner.");

            runner = source;
            initialized = true;
            InitializationError = string.Empty;

            if (!runner.IsServer)
                return true;

            PlayerRef[] activePlayers = runner.ActivePlayers.OrderBy(player => player.PlayerId).ToArray();
            if (!HasCompleteRoleAssignments(activePlayers, acceptedRoles))
                return FailInitialization(
                    $"Host role roster is incomplete or invalid for the current mode. Expected {PhotonLobbyManager.RequiredPlayers} players: {PhotonLobbyManager.GetRequiredRosterDescriptionForDiagnostics()}");

            foreach (PlayerRef player in activePlayers)
            {
                if (!SpawnPlayerOnce(player, acceptedRoles[player]))
                {
                    DespawnAllPlayers();
                    return FailInitialization($"Could not spawn and assign the Host-approved role for {player}.");
                }
            }

            if (!IsRosterSpawnedAndConfirmed(activePlayers, acceptedRoles))
            {
                DespawnAllPlayers();
                return FailInitialization("The complete network player roster was not ready after spawning.");
            }

            return true;
        }

        /// <summary>
        /// Checks this peer's replicated view of every expected player. Used by the Lobby
        /// coordinator as a local readiness predicate; it does not grant match authority.
        /// </summary>
        public bool IsRosterSpawnedAndConfirmed(
            IReadOnlyCollection<PlayerRef> expectedPlayers,
            IReadOnlyDictionary<PlayerRef, NetworkPlayerTeam> acceptedRoles)
        {
            int requiredPlayers = PhotonLobbyManager.RequiredPlayers;
            if (!IsInitialized || expectedPlayers == null || acceptedRoles == null ||
                expectedPlayers.Count != requiredPlayers || acceptedRoles.Count != requiredPlayers)
                return false;

            NetworkPlayerController[] controllers =
                FindObjectsByType<NetworkPlayerController>(FindObjectsSortMode.None);
            HashSet<PlayerRef> expected = new(expectedPlayers);
            HashSet<PlayerRef> found = new();
            int owners = 0;
            int thieves = 0;

            foreach (NetworkPlayerController controller in controllers)
            {
                if (controller == null || controller.Runner != runner ||
                    controller.Object == null || !controller.Object.IsValid)
                    continue;

                PlayerRef inputAuthority = controller.Object.InputAuthority;
                if (!expected.Contains(inputAuthority))
                    continue;
                if (!found.Add(inputAuthority))
                    return false;

                if (!acceptedRoles.TryGetValue(inputAuthority, out NetworkPlayerTeam expectedRole))
                    return false;

                NetworkPlayerRoleState roleState = controller.GetComponent<NetworkPlayerRoleState>();
                if (roleState == null || !roleState.IsRoleConfirmed || roleState.CurrentRole != expectedRole)
                    return false;

                if (expectedRole == NetworkPlayerTeam.OrchardOwner)
                    owners++;
                else if (expectedRole == NetworkPlayerTeam.Thief)
                    thieves++;
                else
                    return false;
            }

            return found.Count == requiredPlayers &&
                   owners == PhotonLobbyManager.RequiredOwners &&
                   thieves == PhotonLobbyManager.RequiredThieves;
        }

        /// <summary>Despawn the Host-owned match players when a pre-play start is aborted.</summary>
        public void DespawnAllPlayers()
        {
            if (runner == null || !runner.IsRunning || !runner.IsServer)
                return;

            HashSet<NetworkObject> objects = new(playerObjects.Values.Where(value => value != null));
            foreach (NetworkObject playerObject in objects)
            {
                if (playerObject.IsValid && playerObject.HasStateAuthority)
                    runner.Despawn(playerObject);
            }

            playerObjects.Clear();
            playerSlots.Clear();
            playersBeingSpawned.Clear();
        }

        public void HandlePlayerJoined(PlayerRef player, NetworkPlayerTeam? acceptedRole)
        {
            if (!initialized || runner == null || !runner.IsRunning || !runner.IsServer)
                return;

            if (!acceptedRole.HasValue || !IsValidRole(acceptedRole.Value))
            {
                Debug.LogError($"Refusing to spawn {player}: no Host-approved role for the current match mode is available.", this);
                return;
            }

            SpawnPlayerOnce(player, acceptedRole.Value);
        }

        public void HandlePlayerLeft(PlayerRef player)
        {
            if (!initialized || runner == null || !runner.IsRunning || !runner.IsServer)
                return;

            NetworkObject playerObject = null;
            if (playerObjects.TryGetValue(player, out NetworkObject knownObject))
                playerObject = knownObject;
            else
                playerObject = FindPlayerObject(player);

            playerObjects.Remove(player);
            playerSlots.Remove(player);
            playersBeingSpawned.Remove(player);

            if (playerObject != null && playerObject.IsValid && playerObject.HasStateAuthority)
                runner.Despawn(playerObject);
        }

        private bool SpawnPlayerOnce(PlayerRef player, NetworkPlayerTeam acceptedRole)
        {
            if (!player.IsValid || playersBeingSpawned.Contains(player))
                return false;

            if (!IsValidRole(acceptedRole))
                return false;

            playersBeingSpawned.Add(player);
            try
            {
                PlayerRef[] staleEntries = playerObjects
                    .Where(entry => entry.Value == null || !entry.Value.IsValid)
                    .Select(entry => entry.Key)
                    .ToArray();
                foreach (PlayerRef stalePlayer in staleEntries)
                {
                    playerObjects.Remove(stalePlayer);
                    playerSlots.Remove(stalePlayer);
                }

                NetworkObject existing = FindPlayerObject(player);
                if (existing != null)
                {
                    if (!AssignAcceptedRole(existing, acceptedRole))
                        return false;

                    playerObjects[player] = existing;
                    playerSlots[player] = FindNearestSpawnSlot(existing.transform.position);
                    return true;
                }

                if (playerObjects.TryGetValue(player, out NetworkObject cached) &&
                    cached != null && cached.IsValid)
                    return AssignAcceptedRole(cached, acceptedRole);

                int slot = FindFreeSpawnSlot();
                if (slot < 0)
                {
                    Debug.LogError($"Cannot spawn {player}: all {SpawnPositions.Length} player slots are occupied.", this);
                    return false;
                }

                playerSlots[player] = slot;
                NetworkObject spawned = runner.Spawn(
                    playerPrefab,
                    SpawnPositions[slot],
                    Quaternion.identity,
                    player);

                if (spawned == null || !spawned.IsValid)
                {
                    playerSlots.Remove(player);
                    Debug.LogError($"Fusion failed to spawn a player object for {player}.", this);
                    return false;
                }

                if (!AssignAcceptedRole(spawned, acceptedRole))
                {
                    playerSlots.Remove(player);
                    runner.Despawn(spawned);
                    return false;
                }

                playerObjects[player] = spawned;
                Debug.Log(
                    $"Spawned {spawned.name} for {player} at {SpawnPositions[slot]} " +
                    $"(Role={acceptedRole}, StateAuthority={spawned.HasStateAuthority}, InputAuthority={spawned.InputAuthority}).",
                    this);
                return true;
            }
            catch (System.Exception exception)
            {
                playerSlots.Remove(player);
                Debug.LogError($"Failed to spawn player {player}: {exception.Message}", this);
                return false;
            }
            finally
            {
                playersBeingSpawned.Remove(player);
            }
        }

        private bool AssignAcceptedRole(NetworkObject playerObject, NetworkPlayerTeam acceptedRole)
        {
            NetworkPlayerRoleState roleState = playerObject.GetComponent<NetworkPlayerRoleState>();
            if (roleState == null || !roleState.AssignRole(acceptedRole))
            {
                Debug.LogError($"Could not assign Host-approved role {acceptedRole} to {playerObject.name}.", this);
                return false;
            }

            return roleState.IsRoleConfirmed && roleState.CurrentRole == acceptedRole;
        }

        private static bool HasCompleteRoleAssignments(
            IReadOnlyCollection<PlayerRef> activePlayers,
            IReadOnlyDictionary<PlayerRef, NetworkPlayerTeam> acceptedRoles)
        {
            int requiredPlayers = PhotonLobbyManager.RequiredPlayers;
            if (activePlayers == null || acceptedRoles == null ||
                activePlayers.Count != requiredPlayers || acceptedRoles.Count != requiredPlayers)
                return false;

            int owners = 0;
            int thieves = 0;
            foreach (PlayerRef player in activePlayers)
            {
                if (!acceptedRoles.TryGetValue(player, out NetworkPlayerTeam role))
                    return false;

                if (role == NetworkPlayerTeam.OrchardOwner)
                    owners++;
                else if (role == NetworkPlayerTeam.Thief)
                    thieves++;
                else
                    return false;
            }

            return owners == PhotonLobbyManager.RequiredOwners &&
                   thieves == PhotonLobbyManager.RequiredThieves;
        }

        private static bool IsValidRole(NetworkPlayerTeam role) =>
            role == NetworkPlayerTeam.Thief ||
            (!PhotonLobbyManager.UseTwoPlayerDemo && role == NetworkPlayerTeam.OrchardOwner);

        private int FindFreeSpawnSlot()
        {
            for (int slot = 0; slot < SpawnPositions.Length; slot++)
            {
                if (!playerSlots.Values.Contains(slot))
                    return slot;
            }

            return -1;
        }

        private int FindNearestSpawnSlot(Vector3 position)
        {
            int nearest = 0;
            float nearestDistance = float.MaxValue;
            for (int slot = 0; slot < SpawnPositions.Length; slot++)
            {
                float distance = (position - SpawnPositions[slot]).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = slot;
                }
            }

            return nearest;
        }

        private NetworkObject FindPlayerObject(PlayerRef player)
        {
            NetworkPlayerController[] controllers =
                FindObjectsByType<NetworkPlayerController>(FindObjectsSortMode.None);

            NetworkObject firstMatch = null;
            foreach (NetworkPlayerController controller in controllers)
            {
                if (controller == null || controller.Object == null || !controller.Object.IsValid ||
                    controller.Object.InputAuthority != player)
                    continue;

                if (firstMatch == null)
                {
                    firstMatch = controller.Object;
                    continue;
                }

                // Repair any duplicate that predates this spawner's local tracking.
                if (runner != null && runner.IsServer && controller.Object.HasStateAuthority)
                    runner.Despawn(controller.Object);
            }

            return firstMatch;
        }

        private bool FailInitialization(string message)
        {
            initialized = false;
            InitializationError = message;
            Debug.LogError(message, this);
            return false;
        }
    }
}
