
using Fusion;
using UnityEngine;

public class MangoDebug : NetworkBehaviour
{
    public override void Spawned()
    {
        Debug.Log(
            $"[MangoDebug] Spawned: {name}, " +
            $"Valid={Object != null && Object.IsValid}, " +
            $"Position={transform.position}",
            this
        );
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        Debug.LogError(
            $"[MangoDebug] Despawned: {name}, " +
            $"HasState={hasState}",
            this
        );
    }

    private void OnDestroy()
    {
        Debug.LogError(
            $"[MangoDebug] OnDestroy: {name}",
            this
        );
    }
}
