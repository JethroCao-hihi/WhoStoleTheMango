using Fusion;
using UnityEngine;

public class NetworkMango : NetworkBehaviour
{
    [Networked]
    private NetworkBool IsCollected { get; set; }

    private Vector3 spawnPosition;
    private float spawnTime;

    public override void Spawned()
    {
        spawnPosition = transform.position;
        spawnTime = Time.time;

        Debug.LogWarning(
            $"[MANGO SPAWNED] Name={name} | " +
            $"Position={transform.position} | " +
            $"StateAuthority={Object.HasStateAuthority} | " +
            $"IsValid={Object.IsValid}",
            this
        );
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null)
            return;

        NetworkPlayerData playerData =
            other.GetComponentInParent<NetworkPlayerData>();

        Debug.LogWarning(
            $"[MANGO COLLISION] " +
            $"Mango={name} | " +
            $"MangoPosition={transform.position} | " +
            $"SpawnPosition={spawnPosition} | " +
            $"TimeSinceSpawn={Time.time - spawnTime:F2}s | " +
            $"Other={other.name} | " +
            $"OtherTag={other.tag} | " +
            $"OtherLayer={LayerMask.LayerToName(other.gameObject.layer)} | " +
            $"OtherPosition={other.transform.position} | " +
            $"Distance={Vector3.Distance(transform.position, other.transform.position):F2} | " +
            $"PlayerDataFound={playerData != null}",
            this
        );

        if (Object == null || !Object.IsValid)
        {
            Debug.LogWarning(
                $"[MANGO] NetworkObject không hợp lệ: {name}",
                this
            );
            return;
        }

        if (!Object.HasStateAuthority)
        {
            Debug.Log(
                $"[MANGO] Bỏ qua collision vì không có State Authority: {name}",
                this
            );
            return;
        }

        if (IsCollected)
        {
            Debug.Log(
                $"[MANGO] Xoài đã được nhặt: {name}",
                this
            );
            return;
        }

        if (playerData == null)
        {
            Debug.Log(
                $"[MANGO] Vật thể {other.name} không phải collider " +
                "của player có NetworkPlayerData.",
                this
            );
            return;
        }

        IsCollected = true;

        Debug.LogError(
            $"[MANGO COLLECTING] " +
            $"Mango={name} | " +
            $"Player={playerData.name} | " +
            $"PlayerPosition={playerData.transform.position} | " +
            $"TimeSinceSpawn={Time.time - spawnTime:F2}s",
            this
        );

        playerData.AddMango();

        Debug.LogError(
            $"[MANGO DESPAWN REQUEST] " +
            $"Runner.Despawn({name})",
            this
        );

        Runner.Despawn(Object);
    }

    public override void Despawned(
        NetworkRunner runner,
        bool hasState)
    {
        Debug.LogWarning(
            $"[MANGO DESPAWNED] {name} | HasState={hasState}",
            this
        );
    }

    private void OnDestroy()
    {
        Debug.LogWarning(
            $"[MANGO DESTROYED] {name}",
            this
        );
    }
}