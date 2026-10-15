using Fusion;
using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class MangoDeliveryPoint : MonoBehaviour
{
    private bool victoryLogged;

    private void Reset()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        box.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        NetworkPlayerData playerData =
            other.GetComponentInParent<NetworkPlayerData>();

        if (playerData == null)
            return;

        // Hàm DepositMangoes tự kiểm tra State Authority.
        int deposited = playerData.DepositMangoes();

        if (deposited <= 0)
            return;

        int teamTotal = GetTeamDepositedTotal();

        Debug.Log(
            $"[Delivery Point] Người chơi giao {deposited} quả | " +
            $"Tổng đội đã giao={teamTotal}/{NetworkPlayerData.TeamMangoGoal}",
            this
        );

        if (teamTotal >= NetworkPlayerData.TeamMangoGoal &&
            !victoryLogged)
        {
            victoryLogged = true;

            Debug.Log(
                "[GAMEPLAY] Đội trộm đã giao đủ 20 quả xoài!",
                this
            );
        }
    }

    private int GetTeamDepositedTotal()
    {
        NetworkPlayerData[] players =
            FindObjectsByType<NetworkPlayerData>(
                FindObjectsSortMode.None
            );

        int total = 0;

        foreach (NetworkPlayerData player in players)
        {
            if (player != null)
                total += player.DepositedMangoCount;
        }

        return total;
    }
}