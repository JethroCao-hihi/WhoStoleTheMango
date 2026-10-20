using Fusion;
using Fusion.Sockets;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

public class PhotonConnectionTest : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Network")]
    [SerializeField] private NetworkObject playerPrefab;
    [SerializeField] private NetworkObject mangoPrefab;

    private NetworkRunner runner;

    private string status = "Chua ket noi";
    private string lastError = "";
    private bool isConnecting;
    private bool runnerShuttingDown;
    private bool mangoesSpawned;
    private bool pendingInteract;
    private bool pendingHarvest;
    private static PhotonConnectionTest instance;
    private const string RoomName = "MangoConnectionTest";

    private readonly List<NetworkObject> spawnedMangoes =
        new List<NetworkObject>();

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;

        if (transform.parent != null)
        {
            Debug.LogError(
                "[Photon Test] PhotonConnectionManager " +
                "phải nằm ở cấp gốc của Hierarchy.",
                this
            );
            return;
        }

        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        if (Keyboard.current == null)
            return;

        // Giữ lại thao tác nhấn nhanh cho đến khi Fusion nhận input.
        if (Keyboard.current.eKey.wasPressedThisFrame)
            pendingInteract = true;

        if (Keyboard.current.fKey.wasPressedThisFrame)
            pendingHarvest = true;
    }

    private static readonly Vector3[] MangoPositions =
    {
        new Vector3(-8f, 0.5f, -8f),
        new Vector3( 8f, 0.5f, -8f),
        new Vector3(-8f, 0.5f,  8f),
        new Vector3( 8f, 0.5f,  8f),
        new Vector3( 0f, 0.5f,  10f)
    };

    // =========================================================
    // CONNECT
    // =========================================================

    private async void StartHostOrClient(GameMode mode)
    {
        if (isConnecting || (runner != null && runner.IsRunning))
            return;

        isConnecting = true;
        lastError = "";
        status = mode == GameMode.Host
            ? "Dang tao phong..."
            : "Dang tham gia phong...";

        Debug.Log($"[Photon Test] Starting {mode}");

        try
        {
            if (runner != null)
                await CleanupRunner();

            mangoesSpawned = false;
            spawnedMangoes.Clear();
            runnerShuttingDown = false;

            runner = gameObject.AddComponent<NetworkRunner>();
            runner.ProvideInput = true;
            runner.AddCallbacks(this);

            NetworkSceneInfo sceneInfo = new NetworkSceneInfo();

            SceneRef sceneRef = SceneRef.FromIndex(
                SceneManager.GetActiveScene().buildIndex
            );

            if (sceneRef.IsValid)
                sceneInfo.AddSceneRef(sceneRef, LoadSceneMode.Single);

            NetworkSceneManagerDefault sceneManager =
                gameObject.AddComponent<NetworkSceneManagerDefault>();

            StartGameResult result = await runner.StartGame(
                new StartGameArgs
                {
                    GameMode = mode,
                    SessionName = RoomName,
                    PlayerCount = 5,
                    Scene = sceneInfo,
                    SceneManager = sceneManager
                }
            );

            if (result.Ok)
            {
                status = mode == GameMode.Host
                    ? "HOST DA TAO PHONG!"
                    : "CLIENT DA THAM GIA!";

                Debug.Log(
                    $"[Photon Test] SUCCESS | Mode={mode} | " +
                    $"Session={runner.SessionInfo.Name} | " +
                    $"RunnerState={runner.State}"
                );

            }
            else
            {
                lastError = result.ShutdownReason.ToString();
                status = "KET NOI THAT BAI";

                Debug.LogError(
                    $"[Photon Test] FAILED | Reason={lastError} | " +
                    $"Message={result.ErrorMessage}"
                );

                await CleanupRunner();
            }
        }
        catch (Exception e)
        {
            lastError = e.Message;
            status = "CO LOI KHI KET NOI";

            Debug.LogException(e);

            await CleanupRunner();
        }
        finally
        {
            isConnecting = false;
        }
    }

    // =========================================================
    // CLEANUP
    // =========================================================

    private async Task CleanupRunner()
    {
        if (runner == null)
        {
            Debug.LogWarning("[DIAGNOSTIC] CleanupRunner: runner is null.", this);
            return;
        }

        var oldRunner = runner;

        Debug.LogError(
            $"[DIAGNOSTIC] CleanupRunner CALLED | " +
            $"IsRunning={oldRunner.IsRunning}",
            this
        );

        runner = null;

        try
        {
            if (oldRunner.IsRunning)
            {
                Debug.LogError("[DIAGNOSTIC] Calling Shutdown().", this);

                await oldRunner.Shutdown();

                Debug.LogError("[DIAGNOSTIC] Shutdown completed.", this);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError(
                $"[DIAGNOSTIC] CleanupRunner exception: {e}",
                this
            );
        }

        if (oldRunner != null)
        {
            Debug.LogWarning("[DIAGNOSTIC] Destroying old Runner.", this);
            Destroy(oldRunner);
        }
    }

    private void OnGUI()
    {
        GUILayout.BeginArea(
            new Rect(20, 20, 300, 220),
            GUI.skin.box
        );

        GUILayout.Label("MANGO CONNECTION TEST");
        GUILayout.Space(10);

        GUILayout.Label("Status: " + status);

        if (!string.IsNullOrEmpty(lastError))
        {
            GUILayout.Label("Error: " + lastError);
        }

        GUILayout.Space(10);

        GUI.enabled = !isConnecting &&
                      (runner == null || !runner.IsRunning);

        if (GUILayout.Button("HOST", GUILayout.Height(45)))
        {
            StartHostOrClient(GameMode.Host);
        }

        if (GUILayout.Button("JOIN", GUILayout.Height(45)))
        {
            StartHostOrClient(GameMode.Client);
        }

        GUI.enabled = true;

        GUILayout.EndArea();
    }


    private void OnDestroy()
    {
        if (instance == this)
            instance = null;

        Debug.LogWarning(
            $"[Photon Test] Manager OnDestroy | " +
            $"Frame={Time.frameCount} | Time={Time.time:F2}s",
            this
        );
    }


    // =========================================================
    // PLAYER
    // =========================================================

    public void OnPlayerJoined(NetworkRunner currentRunner, PlayerRef player)
    {
        Debug.Log($"[Photon] Player joined: {player}");

        if (!currentRunner.IsServer)
            return;

        if (playerPrefab == null)
        {
            Debug.LogError("[Photon] Chua gan Player Prefab!");
            return;
        }

        Vector3 spawnPosition = new Vector3(
            player.PlayerId * 2f,
            1f,
            0f
        );

        NetworkObject playerObject = currentRunner.Spawn(
            playerPrefab,
            spawnPosition,
            Quaternion.identity,
            player
        );

        if (playerObject != null)
        {
            Debug.Log(
                $"[Photon] Spawned {player} | " +
                $"Position={spawnPosition} | " +
                $"Valid={playerObject.IsValid}"
            );
        }
        else
        {
            Debug.LogError($"[Photon] Spawn Player failed: {player}");
        }
    }

    public void OnPlayerLeft(NetworkRunner currentRunner, PlayerRef player)
    {
        Debug.Log($"[Photon] Player left: {player}");
    }

    // =========================================================
    // MANGO SPAWN
    // =========================================================

    private void SpawnInitialMangoes()
    {
        if (mangoesSpawned)
        {
            Debug.LogWarning("[Mango] Mangoes already spawned.");
            return;
        }

        if (runner == null || !runner.IsRunning || !runner.IsServer)
        {
            Debug.LogError("[Mango] Runner chưa sẵn sàng hoặc không phải Host.");
            return;
        }

        if (mangoPrefab == null)
        {
            Debug.LogError("[Mango] Chưa gán Mango Prefab trong Inspector!");
            return;
        }

        MangoTree[] trees =
            FindObjectsByType<MangoTree>(FindObjectsSortMode.None);

        MangoTree tree = null;

        foreach (MangoTree candidate in trees)
        {
            if (candidate != null &&
                candidate.HasClimbPoints &&
                candidate.MangoPoints != null &&
                candidate.MangoPoints.Length > 0)
            {
                tree = candidate;
                break;
            }
        }

        if (tree == null)
        {
            Debug.LogError(
                "[Mango] Không tìm thấy MangoTree có ClimbStartPoint, " +
                "ClimbEndPoint và MangoPoints."
            );
            return;
        }

        spawnedMangoes.Clear();

        foreach (Transform point in tree.MangoPoints)
        {
            if (point == null)
            {
                Debug.LogWarning("[Mango] Có MangoPoint chưa được gán.");
                continue;
            }

            NetworkObject mango = runner.Spawn(
                mangoPrefab,
                point.position,
                Quaternion.identity
            );

            if (mango == null)
            {
                Debug.LogError(
                    $"[Mango] Spawn thất bại tại {point.position}"
                );
                continue;
            }

            spawnedMangoes.Add(mango);

            Debug.Log(
                $"[Mango] Spawned at tree | " +
                $"Point={point.name} | Position={point.position}"
            );
        }

        mangoesSpawned = spawnedMangoes.Count > 0;

        Debug.Log(
            $"[Mango] Spawn hoàn tất: {spawnedMangoes.Count}/" +
            $"{tree.MangoPoints.Length}"
        );

        if (spawnedMangoes.Count > 0)
            StartCoroutine(VerifyMangoes(spawnedMangoes));
    }

    private IEnumerator VerifyMangoes(List<NetworkObject> mangoes)
    {
        yield return new WaitForSeconds(0.5f);

        LogMangoState("0.5 seconds", mangoes);

        yield return new WaitForSeconds(1.5f);

        LogMangoState("2 seconds", mangoes);
    }

    private void LogMangoState(
        string checkpoint,
        List<NetworkObject> mangoes)
    {
        int aliveCount = 0;

        Debug.Log($"[Mango Check] Checkpoint={checkpoint}");

        foreach (NetworkObject mango in mangoes)
        {
            if (mango == null)
            {
                Debug.LogError(
                    "[Mango Check] Object reference is null/destroyed."
                );
                continue;
            }

            bool active = mango.gameObject.activeInHierarchy;
            bool valid = mango.IsValid;

            Debug.Log(
                $"[Mango Check] {mango.name} | " +
                $"Active={active} | Valid={valid} | " +
                $"Position={mango.transform.position} | " +
                $"Scene={mango.gameObject.scene.name}"
            );

            if (active && valid)
                aliveCount++;
        }

        Debug.Log(
            $"[Mango Check] Alive={aliveCount}/{mangoes.Count} " +
            $"at {checkpoint}"
        );

        NetworkObject[] allObjects =
            FindObjectsByType<NetworkObject>(
                FindObjectsSortMode.None
            );

        foreach (NetworkObject obj in allObjects)
        {
            Debug.Log(
                $"[Scene Check] {obj.name} | " +
                $"Active={obj.gameObject.activeInHierarchy} | " +
                $"Position={obj.transform.position}"
            );
        }
    }

    // =========================================================
    // INPUT
    // =========================================================

    public void OnInput(NetworkRunner currentRunner, NetworkInput input)
    {
        Vector2 move = Vector2.zero;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.aKey.isPressed ||
                Keyboard.current.leftArrowKey.isPressed)
                move.x -= 1f;

            if (Keyboard.current.dKey.isPressed ||
                Keyboard.current.rightArrowKey.isPressed)
                move.x += 1f;

            if (Keyboard.current.sKey.isPressed ||
                Keyboard.current.downArrowKey.isPressed)
                move.y -= 1f;

            if (Keyboard.current.wKey.isPressed ||
                Keyboard.current.upArrowKey.isPressed)
                move.y += 1f;
        }

        NetworkButtons buttons = default;

        buttons.Set(PlayerInputButton.Interact, pendingInteract);
        buttons.Set(PlayerInputButton.Harvest, pendingHarvest);

        // Shift là input giữ nút, nên gửi trạng thái đang giữ trực tiếp vào Fusion.
        bool sprintHeld = Keyboard.current != null &&
            (Keyboard.current.leftShiftKey.isPressed ||
             Keyboard.current.rightShiftKey.isPressed);
        buttons.Set(PlayerInputButton.Sprint, sprintHeld);

        input.Set(new NetworkPlayerInput
        {
            Move = Vector2.ClampMagnitude(move, 1f),
            Buttons = buttons,
            CameraYaw = LocalPlayerCameraFollow.CurrentYaw
        });

        // Reset sau khi chuyển input vào Fusion.
        pendingInteract = false;
        pendingHarvest = false;
    }
    public void OnInputMissing(
        NetworkRunner currentRunner,
        PlayerRef player,
        NetworkInput input)
    { }


    public void OnShutdown(
    NetworkRunner currentRunner,
    ShutdownReason shutdownReason)
    {
        // Reason=Ok là đóng bình thường, không cần báo lỗi.
        if (shutdownReason == ShutdownReason.Ok)
            return;

        Debug.LogWarning(
            $"[Photon] Runner shutdown: {shutdownReason}",
            this
        );
    }

    public void OnConnectedToServer(NetworkRunner currentRunner) { }

    public void OnDisconnectedFromServer(
        NetworkRunner currentRunner,
        NetDisconnectReason reason)
    {
        Debug.LogWarning($"[Photon] Disconnected: {reason}");
    }

    public void OnConnectRequest(
        NetworkRunner currentRunner,
        NetworkRunnerCallbackArgs.ConnectRequest request,
        byte[] token)
    {
        request.Accept();
    }

    public void OnConnectFailed(
        NetworkRunner currentRunner,
        NetAddress remoteAddress,
        NetConnectFailedReason reason)
    {
        Debug.LogError($"[Photon] Connect failed: {reason}");
    }

    public void OnUserSimulationMessage(
        NetworkRunner currentRunner,
        SimulationMessagePtr message)
    { }

    public void OnSessionListUpdated(
        NetworkRunner currentRunner,
        List<SessionInfo> sessionList)
    { }

    public void OnCustomAuthenticationResponse(
        NetworkRunner currentRunner,
        Dictionary<string, object> data)
    { }

    public void OnHostMigration(
        NetworkRunner currentRunner,
        HostMigrationToken hostMigrationToken)
    { }

    public void OnReliableDataReceived(
        NetworkRunner currentRunner,
        PlayerRef player,
        ReliableKey key,
        ReadOnlySpan<byte> data)
    { }

    public void OnReliableDataProgress(
        NetworkRunner currentRunner,
        PlayerRef player,
        ReliableKey key,
        float progress)
    { }

    public void OnSceneLoadDone(NetworkRunner currentRunner)
    {
        Debug.Log(
            $"[Scene Debug] Scene load done | " +
            $"Runner={currentRunner.name} | " +
            $"State={currentRunner.State}"
        );

        if (currentRunner != runner)
            return;

        if (!currentRunner.IsServer)
            return;

        if (mangoesSpawned)
            return;

        Debug.Log("[Mango] Scene loaded. Spawn initial mangoes now.");

        SpawnInitialMangoes();
    }

    public void OnSceneLoadStart(NetworkRunner currentRunner)
    {
        Debug.Log($"[Scene Debug] Scene load started: {currentRunner}");
    }

    public void OnObjectExitAOI(
        NetworkRunner currentRunner,
        NetworkObject obj,
        PlayerRef player)
    { }

    public void OnObjectEnterAOI(
        NetworkRunner currentRunner,
        NetworkObject obj,
        PlayerRef player)
    { }
}
