using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// Scene component for a mango tree. Assign climb points and fruit spawn points
/// in the Inspector. This class is intentionally separate from
/// NetworkDroppedMangoMarker, which belongs on the network mango prefab.
/// </summary>
public class MangoTree : MonoBehaviour
{
    private const string NetworkMangoPrefabPath = "Assets/Prefabs/Mango.prefab";
    private const float SpawnPointMatchTolerance = 0.25f;

    [Header("Climb Points")]
    public Transform ClimbStartPoint;
    public Transform ClimbEndPoint;

    [Header("Mango Spawn Points")]
    public Transform[] MangoPoints;

    private readonly Dictionary<int, NetworkObject> spawnedMangoes = new();
    private NetworkRunner initializedRunner;
    private bool initialMangoesSpawned;

    public bool HasConfiguredMangoPoints => MangoPoints != null && MangoPoints.Length > 0 &&
                                            System.Array.TrueForAll(MangoPoints, point => point != null);

    public bool HasClimbPoints => ClimbStartPoint != null && ClimbEndPoint != null;

    /// <summary>Spawns one authoritative network mango at each configured point.</summary>
    public bool EnsureInitialMangoesSpawned(NetworkRunner runner, out string error)
    {
        error = string.Empty;
        if (runner == null || !runner.IsRunning || !runner.IsServer)
        {
            error = "Initial mangoes can only be spawned by the active Host Runner.";
            return false;
        }

        if (!HasConfiguredMangoPoints)
        {
            error = $"MangoTree '{name}' has no complete MangoPoints configuration.";
            return false;
        }

        if (initializedRunner != null && initializedRunner != runner)
        {
            error = $"MangoTree '{name}' is already bound to a different NetworkRunner.";
            return false;
        }

        // A repeated scene callback must never respawn fruit already collected during play.
        if (initializedRunner == runner && initialMangoesSpawned)
            return true;

        if (runner.Prefabs == null)
        {
            error = "The active NetworkRunner has no Fusion NetworkPrefabTable.";
            return false;
        }

        NetworkPrefabId prefabId = runner.Prefabs.GetId(NetworkMangoPrefabPath);
        if (!prefabId.IsValid)
        {
            error = $"{NetworkMangoPrefabPath} is not registered in the active Fusion NetworkPrefabTable.";
            return false;
        }

        initializedRunner = runner;
        for (int i = 0; i < MangoPoints.Length; i++)
        {
            if (spawnedMangoes.TryGetValue(i, out NetworkObject existing) &&
                existing != null && existing.IsValid)
                continue;

            NetworkObject spawned;
            try
            {
                spawned = runner.Spawn(prefabId, MangoPoints[i].position, Quaternion.identity);
            }
            catch (System.Exception exception)
            {
                error = $"Fusion could not spawn mango at point {i + 1}: {exception.Message}";
                DespawnInitialMangoes(runner);
                return false;
            }

            if (spawned == null || !spawned.IsValid || !spawned.HasStateAuthority ||
                spawned.GetComponent<NetworkMango>() == null ||
                spawned.GetComponent<NetworkDroppedMangoMarker>() == null ||
                spawned.GetComponent<NetworkTransform>() == null)
            {
                if (spawned != null && spawned.IsValid)
                    TryDespawn(runner, spawned);
                error = $"Mango prefab at point {i + 1} is missing NetworkObject, NetworkTransform, NetworkMango, or NetworkDroppedMangoMarker.";
                DespawnInitialMangoes(runner);
                return false;
            }

            spawnedMangoes[i] = spawned;
        }

        initialMangoesSpawned = true;
        return true;
    }

    /// <summary>Checks the replicated initial mango set on this peer without creating objects.</summary>
    public bool AreInitialMangoesVisible(NetworkRunner runner)
    {
        if (runner == null || !runner.IsRunning || !HasConfiguredMangoPoints)
            return false;

        NetworkMango[] networkMangoes = FindObjectsByType<NetworkMango>();
        foreach (Transform point in MangoPoints)
        {
            int matches = 0;
            foreach (NetworkMango mango in networkMangoes)
            {
                if (mango == null || mango.Runner != runner || mango.Object == null ||
                    !mango.Object.IsValid ||
                    Vector3.SqrMagnitude(mango.transform.position - point.position) >
                    SpawnPointMatchTolerance * SpawnPointMatchTolerance)
                    continue;

                NetworkDroppedMangoMarker dropped = mango.GetComponent<NetworkDroppedMangoMarker>();
                if (dropped != null && dropped.IsDropped)
                    continue;

                matches++;
            }

            if (matches != 1)
                return false;
        }

        return true;
    }

    /// <summary>Idempotently cleans up this tree's initial fruit on a pre-play abort.</summary>
    public void DespawnInitialMangoes(NetworkRunner runner)
    {
        if (runner == null || !runner.IsRunning || !runner.IsServer ||
            (initializedRunner != null && initializedRunner != runner))
            return;

        foreach (NetworkObject mango in spawnedMangoes.Values)
        {
            if (mango != null && mango.IsValid && mango.HasStateAuthority)
                TryDespawn(runner, mango);
        }

        spawnedMangoes.Clear();
        initializedRunner = null;
        initialMangoesSpawned = false;
    }

    private static void TryDespawn(NetworkRunner runner, NetworkObject mango)
    {
        try
        {
            if (runner != null && runner.IsRunning && mango != null && mango.IsValid &&
                mango.HasStateAuthority)
                runner.Despawn(mango);
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception, mango);
        }
    }

    private void OnDrawGizmos()
    {
        if (ClimbStartPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(ClimbStartPoint.position, 0.12f);
        }

        if (ClimbEndPoint != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(ClimbEndPoint.position, 0.12f);
        }

        if (ClimbStartPoint != null && ClimbEndPoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(ClimbStartPoint.position, ClimbEndPoint.position);
        }

        if (MangoPoints == null)
            return;

        Gizmos.color = new Color(1f, 0.6f, 0f, 1f);
        foreach (Transform point in MangoPoints)
        {
            if (point != null)
                Gizmos.DrawSphere(point.position, 0.08f);
        }
    }
}
