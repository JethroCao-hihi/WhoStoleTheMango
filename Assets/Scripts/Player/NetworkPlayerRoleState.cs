using Fusion;
using UnityEngine;

public enum NetworkPlayerTeam
{
    Thief = 0,
    OrchardOwner = 1
}

/// <summary>
/// Minimal networked role state for the current prototype.
/// The connection test temporarily assigns Host=Owner and joining clients=Thieves.
/// Replace this temporary policy with random role assignment in the lobby later.
/// </summary>
public class NetworkPlayerRoleState : NetworkBehaviour
{
    [SerializeField] private NetworkPlayerTeam startingRole = NetworkPlayerTeam.Thief;

    [Networked] private int RoleCode { get; set; }
    [Networked] private NetworkBool RoleConfirmed { get; set; }

    public NetworkPlayerTeam CurrentRole => (NetworkPlayerTeam)Mathf.Clamp(RoleCode, 0, 1);
    public bool IsRoleConfirmed => RoleConfirmed;
    public bool IsThief => CurrentRole == NetworkPlayerTeam.Thief;
    public bool IsOwner => CurrentRole == NetworkPlayerTeam.OrchardOwner;

    public override void Spawned()
    {
        // The serialized value is a temporary spawn default. Production gameplay uses the
        // Host-approved Lobby role assigned by PlayerSpawner immediately after Spawn returns.
        if (Object.HasStateAuthority)
        {
            RoleCode = (int)startingRole;
            RoleConfirmed = false;
        }
    }

    public bool AssignRole(NetworkPlayerTeam role)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority ||
            (role != NetworkPlayerTeam.Thief && role != NetworkPlayerTeam.OrchardOwner))
            return false;

        RoleCode = (int)role;
        RoleConfirmed = true;
        Debug.Log($"[Role] {name} role set to {role}", this);
        return true;
    }
}
