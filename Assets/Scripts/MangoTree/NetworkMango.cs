using Fusion;
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

        if (!Object.HasStateAuthority || IsCollected)
            return false;

        if (playerData == null)
        {
            Debug.LogWarning("[Mango] Không tìm thấy NetworkPlayerData.", this);
            return false;
        }

        // Chỉ xóa xoài nếu người chơi thực sự nhận được xoài.
        if (!playerData.AddMango())
        {
            Debug.LogWarning(
                "[Mango] Túi xoài đã đầy hoặc không thể cộng điểm.",
                this
            );
            return false;
        }

        IsCollected = true;

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