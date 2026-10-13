using Fusion;

public class NetworkPlayerData : NetworkBehaviour
{
    [Networked]
    public int MangoCount { get; set; }

    public void AddMango()
    {
        if (!Object.HasStateAuthority)
            return;

        MangoCount++;
    }
}