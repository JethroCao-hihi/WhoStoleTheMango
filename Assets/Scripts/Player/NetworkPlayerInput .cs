using Fusion;
using UnityEngine;

/// <summary>Inputs used by the thief character only. Orchard-owner controls are intentionally excluded.</summary>
public enum PlayerInputButton
{
    Interact = 0, // E: climb up/down or pick up dropped mango
    Harvest = 1,  // F: harvest while on a tree
    Sprint = 2,   // Shift: hold to sprint
    Jump = 3,     // Space: long jump
    Crouch = 4,   // Ctrl: crouch/hide
    DropMango = 5,// Q: drop one carried mango
    Rescue = 6,   // R: hold to rescue a captured teammate
    DebugGiveMango = 7 // Editor-only helper: G grants one mango for testing Q
}

public struct NetworkPlayerInput : INetworkInput
{
    public Vector2 Move;
    public NetworkButtons Buttons;
    public float CameraYaw;
}
