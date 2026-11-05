using Fusion;
using Assets.Scripts.GameManager;
using UnityEngine;

public class NetworkMango : NetworkBehaviour
{
    [Networked]
    private NetworkBool IsCollected { get; set; }

    public bool CanBeCollected =>
        Object != null &&
        Object.IsValid &&
        !IsCollected;

    public override void Spawned()
    {
        Debug.Log(
            $"[Mango] Spawned: {name} | " +
            $"Valid={Object.IsValid} | " +
            $"Position={transform.position}",
            this
        );
    }

    public bool TryCollect(NetworkPlayerData playerData)
    {
        if (Object == null || !Object.IsValid)
            return false;

        if (!Object.HasStateAuthority || IsCollected || !NetworkMatchState.IsRunnerInPlayingPhase(Runner))
            return false;

        if (playerData == null || playerData.Runner != Runner || playerData.Object == null ||
            !playerData.Object.IsValid || !playerData.Object.HasStateAuthority)
        {
            Debug.LogWarning("[Mango] Player data is missing, invalid, or belongs to a different Runner.", this);
            return false;
        }

        // Claim synchronously on State Authority before touching inventory so competing
        // trigger/input paths cannot award the same mango twice in one simulation tick.
        IsCollected = true;
        if (!playerData.AddMango())
        {
            IsCollected = false;
            Debug.LogWarning(
                "[Mango] Inventory is full, the player role is invalid, or the gameplay phase is closed.",
                this
            );
            return false;
        }

        Debug.Log(
            $"[Mango] {playerData.name} đã hái được một quả.",
            this
        );

        Runner.Despawn(Object);
        return true;
    }

    public override void Despawned(
        NetworkRunner runner,
        bool hasState)
    {
        Debug.Log(
            $"[Mango] Despawned: {name} | HasState={hasState}",
            this
        );
    }
}
