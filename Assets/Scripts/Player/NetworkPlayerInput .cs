using Fusion;
using UnityEngine;

public enum PlayerInputButton
{
    Interact = 0, // E: leo lên / leo xuống
    Harvest = 1   // F: hái xoài
}

public struct NetworkPlayerInput : INetworkInput
{
    public Vector2 Move;
    public NetworkButtons Buttons;
    public float CameraYaw;
}