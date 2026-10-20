using Fusion;
using Fusion.Sockets;
using System;
using System.Collections;
using System.Collections.Generic;
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
    private bool mangoesSpawned;
    private bool pendingInteract;
    private bool pendingHarvest;
    private bool pendingJump;
    private bool pendingDropMango;
    private bool pendingDebugGiveMango;
    private Vector2 cachedMove;
    private bool sprintHeld;
    private bool crouchHeld;
    private bool rescueHeld;
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
        // Poll the Input System on Unity's frame update, then snapshot it for Fusion ticks.
        // This avoids sampling one-frame key events only inside a network simulation callback.
        Keyboard keyboard = Keyboard.current;
        if (!Application.isFocused || keyboard == null)
        {
            cachedMove = Vector2.zero;
            sprintHeld = false;
            crouchHeld = false;
            rescueHeld = false;
            pendingInteract = false;
            pendingHarvest = false;
            pendingJump = false;
            pendingDropMango = false;
            pendingDebugGiveMango = false;
            return;
        }

        cachedMove = Vector2.zero;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) cachedMove.x -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) cachedMove.x += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) cachedMove.y -= 1f;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) cachedMove.y += 1f;
        cachedMove = Vector2.ClampMagnitude(cachedMove, 1f);

        if (keyboard.eKey.wasPressedThisFrame) pendingInteract = true;
        if (keyboard.fKey.wasPressedThisFrame) pendingHarvest = true;
        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            pendingJump = true;
            Debug.Log("[Input Debug] Space pressed; queued for Fusion.");
        }
        if (keyboard.qKey.wasPressedThisFrame)
        {
            pendingDropMango = true;
            Debug.Log("[Input Debug] Q pressed; queued for Fusion.");
        }
#if UNITY_EDITOR
        if (keyboard.gKey.wasPressedThisFrame)
        {
            pendingDebugGiveMango = true;
            Debug.Log("[Input Debug] G pressed; debug mango grant queued for Fusion.");
        }
#endif

        sprintHeld = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        crouchHeld = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
        rescueHeld = keyboard.rKey.isPressed;
        if (keyboard.leftCtrlKey.wasPressedThisFrame || keyboard.rightCtrlKey.wasPressedThisFrame)
            Debug.Log("[Input Debug] Ctrl pressed; crouch hold state updated.");
    }

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
            // This feature branch implements thief characters only. No Orchard Owner role is assigned here.
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
        NetworkButtons buttons = default;
        buttons.Set(PlayerInputButton.Interact, pendingInteract);
        buttons.Set(PlayerInputButton.Harvest, pendingHarvest);
        buttons.Set(PlayerInputButton.Jump, pendingJump);
        buttons.Set(PlayerInputButton.DropMango, pendingDropMango);
        buttons.Set(PlayerInputButton.Sprint, sprintHeld);
        buttons.Set(PlayerInputButton.Crouch, crouchHeld);
        buttons.Set(PlayerInputButton.Rescue, rescueHeld);
        buttons.Set(PlayerInputButton.DebugGiveMango, pendingDebugGiveMango);

        input.Set(new NetworkPlayerInput
        {
            Move = cachedMove,
            Buttons = buttons,
            CameraYaw = LocalPlayerCameraFollow.CurrentYaw
        });

        if (pendingJump)
            Debug.Log("[Input Debug] Jump button sent to Fusion input.");
        if (pendingDropMango)
            Debug.Log("[Input Debug] DropMango button sent to Fusion input.");
        if (pendingDebugGiveMango)
            Debug.Log("[Input Debug] DebugGiveMango button sent to Fusion input.");

        // Press events persist until at least one Fusion input tick receives them.
        pendingInteract = false;
        pendingHarvest = false;
        pendingJump = false;
        pendingDropMango = false;
        pendingDebugGiveMango = false;
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
