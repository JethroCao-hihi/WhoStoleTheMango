using Fusion;
using MangoHeist.Networking;
using System.Collections.Generic;
using UnityEngine;

public class GameSceneController : NetworkBehaviour
{
    [SerializeField] private PlayerSpawner playerSpawner;

    public override void Spawned()
    {
        if (Runner.IsServer)
        {
            // Lấy danh sách role đã lưu từ phòng chờ hoặc truyền sang, 
            // ở chế độ Demo 2 người, ta có thể khởi tạo danh sách mặc định cho các ActivePlayers:
            var assignedRoles = new Dictionary<PlayerRef, NetworkPlayerTeam>();
            foreach (var player in Runner.ActivePlayers)
            {
                // Mặc định gán Thief cho các player trong chế độ demo 2 người
                assignedRoles[player] = NetworkPlayerTeam.Thief;
            }

            playerSpawner.Initialize(Runner, assignedRoles);
        }
    }
}