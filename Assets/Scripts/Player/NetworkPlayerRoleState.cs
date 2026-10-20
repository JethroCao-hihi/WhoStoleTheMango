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

    public NetworkPlayerTeam CurrentRole => (NetworkPlayerTeam)Mathf.Clamp(RoleCode, 0, 1);
    public bool IsThief => CurrentRole == NetworkPlayerTeam.Thief;
    public bool IsOwner => CurrentRole == NetworkPlayerTeam.OrchardOwner;

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
            RoleCode = (int)startingRole;
    }

    public void AssignRole(NetworkPlayerTeam role)
    {
        if (Object == null || !Object.HasStateAuthority)
            return;

        RoleCode = (int)role;
        Debug.Log($"[Role] {name} role set to {role}", this);
    }
}
