using Fusion;
using Assets.Scripts.GameManager;
using UnityEngine;

public class NetworkPlayerData : NetworkBehaviour
{
    public const int MaxMangoCount = 5;
    public const int TeamMangoGoal = 20;

    // Số xoài đang mang trong túi.
    [Networked]
    public int MangoCount { get; private set; }

    // Tổng số xoài người chơi này đã giao.
    [Networked]
    public int DepositedMangoCount { get; private set; }

    public bool AddMango()
    {
        if (Object == null || !Object.IsValid)
            return false;

        if (!CanChangeGameplayInventory() || !HasConfirmedThiefRole())
            return false;

        if (MangoCount >= MaxMangoCount)
            return false;

        MangoCount++;

        Debug.Log(
            $"[Mango Inventory] {name}: {MangoCount}/{MaxMangoCount}",
            this
        );

        return true;
    }

    /// <summary>Removes one carried mango only after an authoritative drop object was created.</summary>
    public bool TryRemoveMangoForDrop()
    {
        if (!CanChangeGameplayInventory() || !HasConfirmedThiefRole() || MangoCount <= 0)
            return false;

        MangoCount--;
        return true;
    }

    public int DepositMangoes()
    {
        if (Object == null || !Object.IsValid)
            return 0;

        // Chỉ State Authority được phép cập nhật dữ liệu.
        if (!CanChangeGameplayInventory() || !HasConfirmedThiefRole())
            return 0;

        if (MangoCount <= 0)
        {
            Debug.Log("[Mango Deposit] Túi đang trống.", this);
            return 0;
        }

        int amount = MangoCount;

        DepositedMangoCount += amount;
        MangoCount = 0;

        Debug.Log(
            $"[Mango Deposit] {name} giao {amount} quả | " +
            $"Đã giao cá nhân={DepositedMangoCount} | " +
            $"Túi hiện tại={MangoCount}",
            this
        );

        return amount;
    }

    private bool CanChangeGameplayInventory() =>
        Object != null && Object.IsValid && Object.HasStateAuthority && Runner != null && Runner.IsRunning &&
        NetworkMatchState.IsRunnerInPlayingPhase(Runner);

    private bool HasConfirmedThiefRole()
    {
        NetworkPlayerRoleState roleState = GetComponent<NetworkPlayerRoleState>();
        return roleState != null && roleState.IsRoleConfirmed && roleState.IsThief;
    }
}
