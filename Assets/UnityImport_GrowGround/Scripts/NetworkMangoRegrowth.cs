using Fusion;
using UnityEngine;
using Assets.Scripts.GameManager;

/// <summary>Optional Fusion regrowth component. Add beside MangoTree on each tree object.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MangoTree))]
public sealed class NetworkMangoRegrowth : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float regrowSeconds = 20f;
    [SerializeField, Min(0.1f)] private float scanInterval = 0.5f;

    private const string MangoPrefabPath = "Assets/Prefabs/Mango.prefab";
    private const float SlotTolerance = 0.25f;
    private MangoTree tree;
    private float[] regrowAt;
    private bool[] slotWasEmpty;
    private float nextScan;

    private void Awake()
    {
        tree = GetComponent<MangoTree>();
        ResizeState();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + Mathf.Max(0.1f, scanInterval);
        if (tree == null || !tree.HasConfiguredMangoPoints) return;
        ResizeState();

        NetworkRunner runner = FindRunningRunner();
        if (runner == null || !runner.IsServer) return;

        for (int i = 0; i < tree.MangoPoints.Length; i++)
        {
            bool occupied = HasMangoAt(runner, tree.MangoPoints[i].position);
            if (occupied)
            {
                slotWasEmpty[i] = false;
                continue;
            }
            if (!slotWasEmpty[i])
            {
                slotWasEmpty[i] = true;
                regrowAt[i] = Time.unscaledTime + Mathf.Max(0.1f, regrowSeconds);
                continue;
            }
            if (Time.unscaledTime >= regrowAt[i]) TrySpawn(runner, tree.MangoPoints[i].position, i);
        }
    }

    private void ResizeState()
    {
        int count = tree != null && tree.MangoPoints != null ? tree.MangoPoints.Length : 0;
        if (regrowAt != null && regrowAt.Length == count) return;
        regrowAt = new float[count];
        slotWasEmpty = new bool[count];
    }

    private static NetworkRunner FindRunningRunner()
    {
        NetworkRunner[] runners = FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None);
        foreach (NetworkRunner candidate in runners)
            if (candidate != null && candidate.IsRunning &&
                NetworkMatchState.IsRunnerInPlayingPhase(candidate)) return candidate;
        return null;
    }

    private static bool HasMangoAt(NetworkRunner runner, Vector3 position)
    {
        NetworkMango[] mangoes = FindObjectsByType<NetworkMango>(FindObjectsSortMode.None);
        foreach (NetworkMango mango in mangoes)
        {
            if (mango == null || mango.Runner != runner || mango.Object == null || !mango.Object.IsValid ||
                (mango.transform.position - position).sqrMagnitude > SlotTolerance * SlotTolerance) continue;
            NetworkDroppedMangoMarker marker = mango.GetComponent<NetworkDroppedMangoMarker>();
            if (marker == null || !marker.IsDropped) return true;
        }
        return false;
    }

    private static void TrySpawn(NetworkRunner runner, Vector3 position, int slot)
    {
        if (runner.Prefabs == null) return;
        NetworkPrefabId prefabId = runner.Prefabs.GetId(MangoPrefabPath);
        if (!prefabId.IsValid)
        {
            Debug.LogError($"[MangoRegrowth] {MangoPrefabPath} chưa được đăng ký trong Fusion NetworkPrefabTable.");
            return;
        }
        try
        {
            NetworkObject spawned = runner.Spawn(prefabId, position, Quaternion.identity);
            if (spawned == null || !spawned.IsValid || !spawned.HasStateAuthority ||
                spawned.GetComponent<NetworkMango>() == null || spawned.GetComponent<NetworkTransform>() == null)
            {
                if (spawned != null && spawned.IsValid && spawned.HasStateAuthority) runner.Despawn(spawned);
                Debug.LogError($"[MangoRegrowth] Spawn prefab xoài thất bại tại slot {slot}.");
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogError($"[MangoRegrowth] Không thể spawn xoài tại slot {slot}: {exception.Message}");
        }
    }
}
