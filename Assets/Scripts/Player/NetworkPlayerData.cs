using Fusion;
using UnityEngine;

public class NetworkPlayerData : NetworkBehaviour
{
    public const int MaxMangoCount = 5;
    public const int TeamMangoGoal = 20;

    // Số xoài đang mang trong túi.
    [Networked]
    public int MangoCount { get; set; }

    // Tổng số xoài người chơi này đã giao.
    [Networked]
    public int DepositedMangoCount { get; set; }

    public bool AddMango()
    {
        if (Object == null || !Object.IsValid)
            return false;

        if (!Object.HasStateAuthority)
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

    public int DepositMangoes()
    {
        if (Object == null || !Object.IsValid)
            return 0;

        // Chỉ State Authority được phép cập nhật dữ liệu.
        if (!Object.HasStateAuthority)
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
}