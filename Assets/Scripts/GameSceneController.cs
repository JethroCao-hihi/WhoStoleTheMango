using Fusion;
using UnityEngine;

public class GameSceneController : NetworkBehaviour
{
    public override void Spawned()
    {
        // PhotonLobbyManager owns the lobby-approved roster and initializes PlayerSpawner.
        // This scene object must not assign every player to Thief and overwrite that roster.
    }
}