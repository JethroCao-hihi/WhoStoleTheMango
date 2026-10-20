using Fusion;
using UnityEngine;

public enum PlayerInputButton
{
    Interact = 0, // E: leo len / leo xuong
    Harvest = 1,  // F: hai xoai
    Sprint = 2    // Shift: chay nhanh (giu nut)
}

public struct NetworkPlayerInput : INetworkInput
{
    public Vector2 Move;
    public NetworkButtons Buttons;
    public float CameraYaw;
}
