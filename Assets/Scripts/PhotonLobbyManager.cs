using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Assets.Scripts.GameManager;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace MangoHeist.Networking
{
    public enum PhotonLobbyState
    {
        Idle,
        Connecting,
        InRoom,
        ShuttingDown,
        Error
    }

    /// <summary>
    /// Owns the production Runner, Lobby role roster, synchronized scene flow, and pre-play readiness.
    /// PhotonConnectionTest remains a separate diagnostic tool and is rejected if its Runner exists.
    /// </summary>
    public sealed class PhotonLobbyManager : MonoBehaviour, INetworkRunnerCallbacks
    {
        // Official production limits. Keep these values unchanged for the full 5-player mode.
        public const int MaximumPlayers = 5;
        private const int MaximumThieves = 4;

        // TEMPORARY DEMO SWITCH: true = 1 OrchardOwner + 1 Thief; false = official 1 Owner + 4 Thieves.
        // Build Host and Client from the same setting so both peers use the same roster rules.
        public const bool UseTwoPlayerDemo = true;

        public static int RequiredOwners => 1;
        public static int RequiredThieves => UseTwoPlayerDemo ? 1 : MaximumThieves;
        public static int RequiredPlayers => RequiredOwners + RequiredThieves;
        public static int RoomCapacity => RequiredPlayers;
        private const float MatchReadinessTimeoutSeconds = 30f;
        private const float LobbyReturnLoadTimeoutSeconds = 30f;
        private const float LobbyReturnAckTimeoutSeconds = 15f;
        private const string MenuSceneName = "MenuScene";
        private const string LobbySceneName = "LobbyScene";
        private const string LobbyScenePath = "Assets/Scenes/LobbyScene.unity";
        private const string GameSceneName = "GameScene";
        private const string GameScenePath = "Assets/Scenes/GameScene.unity";
        private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private const byte RoleProtocolVersion = 1;
        private const byte RoleRequestMessageType = 1;
        private const byte RoleRosterMessageType = 2;
        private const byte MatchReadinessChallengeMessageType = 3;
        private const byte MatchReadinessAckMessageType = 4;
        private const byte MatchReadinessFailedMessageType = 5;
        private const byte LobbyReturnChallengeMessageType = 6;
        private const byte LobbyReturnAckMessageType = 7;
        private const byte LobbyReturnCompleteMessageType = 8;
        private const byte UnselectedRoleCode = byte.MaxValue;
        private const int RoleRosterHeaderSize = 9;
        private const int RoleRosterEntrySize = 5;
        private static readonly ReliableKey RoleRequestReliableKey = ReliableKey.FromInts(0x574D524C, 0x52455131, 0, 0);
        private static readonly ReliableKey RoleRosterReliableKey = ReliableKey.FromInts(0x574D524C, 0x52535431, 0, 0);
        private static readonly ReliableKey MatchReadinessReliableKey = ReliableKey.FromInts(0x574D524C, 0x52445931, 0, 0);

        private enum RoleRequestStatus : byte
        {
            Snapshot = 0,
            Accepted = 1,
            Rejected = 2
        }

        private enum RoleRequestRejection : byte
        {
            None = 0,
            InvalidMessage = 1,
            InvalidRole = 2,
            PlayerNotInSession = 3,
            OwnerSlotTaken = 4,
            ThiefSlotsFull = 5,
            LobbyClosed = 6
        }

        private static PhotonLobbyManager instance;
        private readonly System.Threading.SemaphoreSlim lifecycleGate = new(1, 1);
        private readonly List<string> playerLabels = new();
        private readonly Dictionary<PlayerRef, NetworkPlayerTeam> acceptedRoleRoster = new();
        private readonly HashSet<PlayerRef> matchReadyPlayers = new();
        private readonly HashSet<PlayerRef> lobbyReturnReadyPlayers = new();
        private NetworkRunner runner;
        private NetworkSceneManagerDefault networkSceneManager;
        private PlayerSpawner playerSpawner;
        private Assets.Scripts.GameManager.NetworkMatchState matchState;
        private MangoTree[] mangoTrees = Array.Empty<MangoTree>();
        private GameObject runnerRoot;
        private bool operationInProgress;
        private bool startGameRequested;
        private bool matchSceneLoadDoneLocally;
        private bool matchSceneReadyLocally;
        private bool readinessChallengeSent;
        private bool readinessAckSent;
        private bool readinessFailureSent;
        private bool readinessTimeoutActive;
        private double readinessDeadlineSimulationTime;
        private float nextClientReadinessDiagnosticTime;
        private const float ClientSceneObjectAttachTimeoutSeconds = 15f;
        private float clientSceneObjectAttachDeadline;
        private bool matchSceneSetupPending;
        private bool matchAbortRequested;
        private bool returnToLobbyRequested;
        private bool returnToLobbyLoadStarted;
        private bool lobbySceneLoadDoneLocally;
        private bool lobbyReturnChallengeSent;
        private bool lobbyReturnAckSent;
        private bool clientLobbyReturnInProgress;
        private int clientLobbyReturnAttemptId;
        private int highestClientLobbyReturnAttemptId;
        private bool lobbyReturnAckTimeoutActive;
        private double lobbyReturnAckDeadlineSimulationTime;
        private double lobbySceneLoadDeadlineSimulationTime;
        private int matchStartAttemptId;
        private int clientReadinessAttemptId;
        private int highestClientReadinessAttemptId;
        private string matchSceneFailure;
        private string matchAbortReason;
        private bool leaveRequested;
        private bool shuttingDown;
        private bool unexpectedCleanupStarted;
        private string callbackFailure;
        private bool pendingInteract;
        private bool pendingHarvest;
        private bool pendingJump;
        private bool pendingDropMango;
        private bool pendingDebugGiveMango;
        private bool pendingGardenTrap;
        private bool pendingGardenDog;
        private bool pendingGardenSlipper;
        private bool sprintHeld;
        private bool crouchHeld;
        private bool rescueHeld;
        private Vector2 cachedMove;
        private bool roleRequestPending;
        private string roleSelectionMessage = UseTwoPlayerDemo
            ? "Choose OrchardOwner or Thief. The 2-player demo requires one of each."
            : "Choose a role. The Host must confirm your selection.";

        public static PhotonLobbyManager Existing => instance;
        public PhotonLobbyState State { get; private set; } = PhotonLobbyState.Idle;
        public string StatusMessage { get; private set; } = "Not connected";
        public string RoomCode { get; private set; } = string.Empty;
        public bool IsBusy => operationInProgress;
        public bool IsInRoom => State == PhotonLobbyState.InRoom && runner != null && runner.IsRunning;
        public bool IsHost => IsInRoom && runner.IsServer;
        public bool CanChooseRole => IsInRoom && !operationInProgress && !startGameRequested &&
                                     !clientLobbyReturnInProgress &&
                                     SceneManager.GetActiveScene().name == LobbySceneName;
        public bool IsRoleRequestPending => roleRequestPending;
        public string RoleSelectionMessage => roleSelectionMessage;
        public string OwnerSlotStatus => GetOwnerSlotStatus();
        public string ThiefSlotStatus => $"Thief slots: {RequiredThieves - GetAssignedRoleCount(NetworkPlayerTeam.Thief)}/{RequiredThieves} open";
        public NetworkPlayerTeam? LocalAcceptedRole =>
            runner != null && acceptedRoleRoster.TryGetValue(runner.LocalPlayer, out NetworkPlayerTeam role)
                ? role
                : null;
        public bool HasValidRoleRoster => HasCompleteRoleRoster(runner);
        public bool CanStartGame => IsHost && !operationInProgress && !startGameRequested && HasValidRoleRoster;
        public IReadOnlyList<string> PlayerLabels => playerLabels;
        public event Action Changed;

        public static PhotonLobbyManager EnsureInstance()
        {
            if (instance != null)
                return instance;

            instance = FindAnyObjectByType<PhotonLobbyManager>();
            if (instance != null)
                return instance;

            var root = new GameObject("[PhotonLobbyManager]");
            return root.AddComponent<PhotonLobbyManager>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        private void Update()
        {
            if (runner == null || !runner.IsRunning)
            {
                ClearGameplayInput();
                return;
            }

            if (SceneManager.GetActiveScene().name == GameSceneName)
            {
                Keyboard keyboard = Keyboard.current;
                if (!Application.isFocused || keyboard == null)
                {
                    ClearGameplayInput();
                }
                else
                {
                    cachedMove = Vector2.zero;
                    if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) cachedMove.x -= 1f;
                    if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) cachedMove.x += 1f;
                    if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) cachedMove.y -= 1f;
                    if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) cachedMove.y += 1f;
                    cachedMove = Vector2.ClampMagnitude(cachedMove, 1f);

                    if (keyboard.eKey.wasPressedThisFrame) pendingInteract = true;
                    if (keyboard.fKey.wasPressedThisFrame) pendingHarvest = true;
                    if (keyboard.spaceKey.wasPressedThisFrame) pendingJump = true;
                    if (keyboard.qKey.wasPressedThisFrame) pendingDropMango = true;
                    if (keyboard.qKey.wasPressedThisFrame) pendingGardenTrap = true;
                    if (keyboard.eKey.wasPressedThisFrame) pendingGardenDog = true;
                    if (keyboard.rKey.wasPressedThisFrame) pendingGardenSlipper = true;
#if UNITY_EDITOR
                    if (keyboard.gKey.wasPressedThisFrame) pendingDebugGiveMango = true;
#endif

                    sprintHeld = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                    crouchHeld = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
                    rescueHeld = keyboard.rKey.isPressed;
                }

                TryCompletePendingGameSceneSetup();
                UpdateMatchLifecycle();
                return;
            }

            if (SceneManager.GetActiveScene().name == LobbySceneName &&
                (returnToLobbyRequested || clientLobbyReturnInProgress))
            {
                ClearGameplayInput();
                UpdateMatchLifecycle();
                return;
            }

            ClearGameplayInput();
        }

        private void ClearGameplayInput()
        {
            cachedMove = Vector2.zero;
            pendingInteract = false;
            pendingHarvest = false;
            pendingJump = false;
            pendingDropMango = false;
            pendingDebugGiveMango = false;
            pendingGardenTrap = false;
            pendingGardenDog = false;
            pendingGardenSlipper = false;
            sprintHeld = false;
            crouchHeld = false;
            rescueHeld = false;
        }

        public void CreateRoom(string requestedCode)
        {
            if (operationInProgress || leaveRequested || IsInRoom)
                return;

            string code = string.IsNullOrWhiteSpace(requestedCode)
                ? GenerateRoomCode()
                : requestedCode.Trim().ToUpperInvariant();

            if (!IsValidRoomCode(code))
            {
                SetState(PhotonLobbyState.Error,
                    "Room code must contain 4–12 letters or numbers (excluding ambiguous characters).");
                return;
            }

            _ = StartSessionAsync(GameMode.Host, code);
        }

        public void JoinRoom(string requestedCode)
        {
            if (operationInProgress || leaveRequested || IsInRoom)
                return;

            string code = (requestedCode ?? string.Empty).Trim().ToUpperInvariant();
            if (!IsValidRoomCode(code))
            {
                SetState(PhotonLobbyState.Error, "Enter a valid room code (4–12 letters or numbers).");
                return;
            }

            _ = StartSessionAsync(GameMode.Client, code);
        }

        public void RequestRoleSelection(NetworkPlayerTeam requestedRole)
        {
            if (!CanChooseRole)
                return;

            if (!IsValidRole(requestedRole))
            {
                roleSelectionMessage = "That role selection is invalid.";
                Changed?.Invoke();
                return;
            }

            if (runner.IsServer)
            {
                ProcessRoleRequest(runner.LocalPlayer, requestedRole);
                return;
            }

            if (roleRequestPending)
                return;

            roleRequestPending = true;
            roleSelectionMessage = "Role request sent. Waiting for Host confirmation.";
            Changed?.Invoke();

            byte[] payload =
            {
                RoleProtocolVersion,
                RoleRequestMessageType,
                (byte)requestedRole
            };

            try
            {
                runner.SendReliableDataToServer(RoleRequestReliableKey, payload);
            }
            catch (Exception exception)
            {
                roleRequestPending = false;
                roleSelectionMessage = $"Could not send role request: {exception.Message}";
                Changed?.Invoke();
            }
        }

        public bool TryGetAcceptedRole(PlayerRef player, out NetworkPlayerTeam role) =>
            acceptedRoleRoster.TryGetValue(player, out role);

        public void StartGame()
        {
            // This check runs in the service too; UI visibility is not an authority boundary.
            if (!CanStartGame)
            {
                if (IsHost && !HasValidRoleRoster)
                    StatusMessage = $"Cannot start: {GetRequiredRosterDescription()}";
                Changed?.Invoke();
                return;
            }

            int buildIndex = SceneUtility.GetBuildIndexByScenePath(GameScenePath);
            if (buildIndex < 0)
            {
                StatusMessage = $"Cannot start: {GameScenePath} is not enabled in Build Settings.";
                Changed?.Invoke();
                return;
            }

            SceneRef gameScene = SceneRef.FromIndex(buildIndex);
            if (!gameScene.IsValid)
            {
                StatusMessage = "Cannot start: Fusion could not resolve the GameScene build index.";
                Changed?.Invoke();
                return;
            }

            ResetMatchReadinessState();
            matchStartAttemptId = unchecked(matchStartAttemptId + 1);
            if (matchStartAttemptId == 0)
                matchStartAttemptId = 1;
            startGameRequested = true;
            StatusMessage = "Host is starting the game…";
            Changed?.Invoke();

            try
            {
                // Fusion's scene manager synchronizes this load to all peers in the session.
                NetworkSceneAsyncOp loadOperation = runner.LoadScene(gameScene, LoadSceneMode.Single);
                if (!loadOperation.IsValid)
                {
                    startGameRequested = false;
                    StatusMessage = $"Could not start GameScene: {loadOperation.Error}";
                    Changed?.Invoke();
                    return;
                }

                _ = ObserveSceneLoadAsync(runner, loadOperation);
            }
            catch (Exception exception)
            {
                startGameRequested = false;
                StatusMessage = $"Could not start GameScene: {exception.Message}";
                Changed?.Invoke();
                Debug.LogException(exception, this);
            }
        }

        private async Task ObserveSceneLoadAsync(NetworkRunner source, NetworkSceneAsyncOp loadOperation)
        {
            while (!loadOperation.IsDone)
            {
                if (source == null || source != runner || !source.IsRunning)
                    return;
                await Task.Yield();
            }

            if (source != runner || !startGameRequested || loadOperation.Error == null)
                return;

            startGameRequested = false;
            matchAbortRequested = false;
            matchAbortReason = null;
            StatusMessage = $"GameScene failed to load: {loadOperation.Error}";
            Changed?.Invoke();
        }

        public Task LeaveRoomAsync() => LeaveCoreAsync(returnToMenu: false);

        public Task LeaveAndReturnToMenuAsync() => LeaveCoreAsync(returnToMenu: true);

        private async Task StartSessionAsync(GameMode gameMode, string code)
        {
            operationInProgress = true;
            startGameRequested = false;
            callbackFailure = null;
            ResetMatchReadinessState();
            matchStartAttemptId = 0;
            clientReadinessAttemptId = 0;
            highestClientReadinessAttemptId = 0;
            ClearRoleSelections();
            SetState(PhotonLobbyState.Connecting,
                gameMode == GameMode.Host ? $"Creating room {code}…" : $"Joining room {code}…");

            bool gateEntered = false;
            try
            {
                await lifecycleGate.WaitAsync();
                gateEntered = true;

                if (runner != null)
                    await ShutdownRunnerAsync();

                NetworkRunner[] existingRunners = FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None);
                if (existingRunners.Any(existing => existing != null))
                    throw new InvalidOperationException(
                        "Another NetworkRunner already exists. Close the NetworkingTest session before using Lobby.");

                RoomCode = code;
                Changed?.Invoke();
                runnerRoot = new GameObject("[Photon Lobby Runner]");
                DontDestroyOnLoad(runnerRoot);
                runner = runnerRoot.AddComponent<NetworkRunner>();
                // ProvideInput must be enabled on every peer for its own Input Authority player.
                // The callback sends neutral input while in Lobby and samples controls in GameScene.
                runner.ProvideInput = true;
                runner.AddCallbacks(this);
                networkSceneManager = runnerRoot.AddComponent<NetworkSceneManagerDefault>();

                // Register the currently active Lobby scene as Fusion's startup scene.
                // In Host mode this lets Fusion attach Lobby scene NetworkObjects and publish
                // the authoritative scene information to joining Clients.
                Scene activeStartupScene = SceneManager.GetActiveScene();
                if (!activeStartupScene.IsValid() || activeStartupScene.buildIndex < 0)
                {
                    throw new InvalidOperationException(
                        $"Cannot start Fusion from invalid scene '{activeStartupScene.name}'.");
                }

                NetworkSceneInfo startupSceneInfo = new NetworkSceneInfo();
                startupSceneInfo.AddSceneRef(
                    SceneRef.FromIndex(activeStartupScene.buildIndex),
                    LoadSceneMode.Single);

                StartGameResult result = await runner.StartGame(new StartGameArgs
                {
                    GameMode = gameMode,
                    SessionName = code,
                    // Fusion PlayerCount is the maximum number of peers in the room, including the Host.
                    PlayerCount = RoomCapacity,
                    IsOpen = true,
                    IsVisible = false,
                    Scene = startupSceneInfo,
                    SceneManager = networkSceneManager
                });

                if (!result.Ok)
                {
                    string message = FormatStartError(result.ShutdownReason, result.ErrorMessage);
                    await ShutdownRunnerAsync();
                    RoomCode = string.Empty;
                    startGameRequested = false;
                    ClearPlayers();
                    SetState(PhotonLobbyState.Error, message);
                    return;
                }

                if (leaveRequested)
                    return;

                if (!string.IsNullOrEmpty(callbackFailure))
                {
                    string message = callbackFailure;
                    await ShutdownRunnerAsync();
                    RoomCode = string.Empty;
                    ClearPlayers();
                    SetState(PhotonLobbyState.Error, message);
                    return;
                }

                RoomCode = string.IsNullOrEmpty(runner.SessionInfo.Name) ? code : runner.SessionInfo.Name;
                ReconcileRoleRoster(runner);
                RefreshPlayers(runner);
                if (runner.IsServer)
                    SendRoleRosterSnapshot(-1, RoleRequestStatus.Snapshot, RoleRequestRejection.None);
                SetState(PhotonLobbyState.InRoom, $"Connected to room {RoomCode}.");

                // >>> BỔ SUNG ĐOẠN NÀY ĐỂ TỰ ĐỘNG CHUYỂN SANG LOBBY SCENE KHI LÀ HOST <<<
                if (runner.IsServer && SceneManager.GetActiveScene().name == MenuSceneName)
                {
                    int lobbyBuildIndex = SceneUtility.GetBuildIndexByScenePath(LobbyScenePath);
                    if (lobbyBuildIndex >= 0)
                    {
                        runner.LoadScene(SceneRef.FromIndex(lobbyBuildIndex), LoadSceneMode.Single);
                    }
                    else
                    {
                        Debug.LogError("LobbyScene path is not valid or missing in Build Settings!");
                    }
                }
            }
            catch (Exception exception)
            {
                string message = $"Photon connection failed: {exception.Message}";
                try
                {
                    await ShutdownRunnerAsync();
                }
                catch (Exception shutdownException)
                {
                    message += $" Shutdown also failed: {shutdownException.Message}";
                }

                RoomCode = string.Empty;
                startGameRequested = false;
                ClearPlayers();
                SetState(PhotonLobbyState.Error, message);
                Debug.LogException(exception, this);
            }
            finally
            {
                operationInProgress = leaveRequested;
                if (gateEntered)
                    lifecycleGate.Release();
                Changed?.Invoke();
            }
        }

        private async Task LeaveCoreAsync(bool returnToMenu)
        {
            leaveRequested = true;
            operationInProgress = true;
            shuttingDown = true;
            SetState(PhotonLobbyState.ShuttingDown,
                returnToMenu ? "Leaving room and returning to Menu…" : "Leaving room…");

            bool gateEntered = false;
            try
            {
                // If StartGame is still pending, wait for it to resolve before shutting down the Runner.
                await lifecycleGate.WaitAsync();
                gateEntered = true;
                await ShutdownRunnerAsync();
                RoomCode = string.Empty;
                startGameRequested = false;
                ClearPlayers();
                SetState(PhotonLobbyState.Idle, "Not connected");
            }
            catch (Exception exception)
            {
                SetState(PhotonLobbyState.Error, $"Could not leave the Photon room cleanly: {exception.Message}");
                Debug.LogException(exception, this);
            }
            finally
            {
                if (gateEntered)
                    lifecycleGate.Release();
                shuttingDown = false;
                operationInProgress = false;
                leaveRequested = false;
                Changed?.Invoke();
            }

            if (returnToMenu && State == PhotonLobbyState.Idle)
                SceneManager.LoadScene(MenuSceneName);
        }

        private async Task ShutdownRunnerAsync()
        {
            NetworkRunner oldRunner = runner;
            GameObject oldRoot = runnerRoot;
            if (oldRunner == null)
            {
                if (oldRoot != null)
                    Destroy(oldRoot);
                runnerRoot = null;
                return;
            }

            shuttingDown = true;
            try
            {
                oldRunner.RemoveCallbacks(this);
                // Shutdown initiates disposal; await completion before disabling/destroying the Runner root.
                await oldRunner.Shutdown(false);
            }
            finally
            {
                runner = null;
                networkSceneManager = null;
                runnerRoot = null;
                if (oldRoot != null)
                {
                    oldRoot.SetActive(false);
                    Destroy(oldRoot);
                }
                shuttingDown = false;
            }
        }

        private async Task CleanupUnexpectedRunnerAsync(NetworkRunner endedRunner)
        {
            bool wasInGameScene = SceneManager.GetActiveScene().name == GameSceneName;
            operationInProgress = true;
            SetState(PhotonLobbyState.Error, callbackFailure ?? "Photon disconnected unexpectedly.");
            bool gateEntered = false;
            try
            {
                await lifecycleGate.WaitAsync();
                gateEntered = true;
                if (runner == endedRunner)
                    await ShutdownRunnerAsync();
                RoomCode = string.Empty;
                startGameRequested = false;
                ClearPlayers();
                // Keep the Lobby visible after an unexpected disconnect so the user can see
                // the actual failure and start a new session. Explicit Leave still uses
                // LeaveCoreAsync and intentionally returns to Menu.
                if (wasInGameScene)
                    SceneManager.LoadScene(MenuSceneName);
                else if (SceneManager.GetActiveScene().name == LobbySceneName)
                {
                    SetState(PhotonLobbyState.Error,
                        callbackFailure ?? "Photon disconnected while the role roster was being updated.");
                    Changed?.Invoke();
                }
            }
            catch (Exception exception)
            {
                SetState(PhotonLobbyState.Error, $"Disconnected; cleanup failed: {exception.Message}");
                Debug.LogException(exception, this);
            }
            finally
            {
                unexpectedCleanupStarted = false;
                operationInProgress = false;
                if (gateEntered)
                    lifecycleGate.Release();
                Changed?.Invoke();
            }
        }

        private void HandleUnexpectedEnd(NetworkRunner currentRunner, string message)
        {
            if (currentRunner != runner || shuttingDown)
                return;

            callbackFailure = message;
            if (operationInProgress)
            {
                SetState(PhotonLobbyState.Error, message);
                return;
            }

            if (unexpectedCleanupStarted)
                return;

            unexpectedCleanupStarted = true;
            _ = CleanupUnexpectedRunnerAsync(currentRunner);
        }

        private bool IsRoleSelectionOpen(NetworkRunner source) =>
            source != null && source == runner && source.IsRunning && source.IsServer &&
            !operationInProgress && !startGameRequested &&
            SceneManager.GetActiveScene().name == LobbySceneName;

        private bool HasCompleteRoleRoster(NetworkRunner source)
        {
            if (source == null || source != runner || !source.IsRunning || !source.IsServer)
                return false;

            PlayerRef[] activePlayers = source.ActivePlayers.ToArray();
            if (activePlayers.Length != RequiredPlayers)
                return false;

            int ownerCount = 0;
            int thiefCount = 0;
            foreach (PlayerRef player in activePlayers)
            {
                if (!acceptedRoleRoster.TryGetValue(player, out NetworkPlayerTeam role))
                    return false;

                if (role == NetworkPlayerTeam.OrchardOwner)
                    ownerCount++;
                else if (role == NetworkPlayerTeam.Thief)
                    thiefCount++;
                else
                    return false;
            }

            return ownerCount == RequiredOwners && thiefCount == RequiredThieves;
        }

        private int GetAssignedRoleCount(NetworkPlayerTeam role)
        {
            if (runner == null || !runner.IsRunning)
                return 0;

            HashSet<PlayerRef> activePlayers = new(runner.ActivePlayers);
            return acceptedRoleRoster.Count(entry => activePlayers.Contains(entry.Key) && entry.Value == role);
        }

        private string GetOwnerSlotStatus()
        {
            if (runner == null || !runner.IsRunning)
                return "OrchardOwner slot: open";

            foreach (PlayerRef player in runner.ActivePlayers)
            {
                if (acceptedRoleRoster.TryGetValue(player, out NetworkPlayerTeam role) &&
                    role == NetworkPlayerTeam.OrchardOwner)
                    return $"OrchardOwner slot: Player {player.PlayerId + 1}";
            }

            return "OrchardOwner slot: open";
        }

        private void ReconcileRoleRoster(NetworkRunner source)
        {
            if (source == null || source != runner || !source.IsServer)
                return;

            HashSet<PlayerRef> activePlayers = new(source.ActivePlayers);
            PlayerRef[] departed = acceptedRoleRoster.Keys
                .Where(player => !activePlayers.Contains(player))
                .ToArray();
            foreach (PlayerRef player in departed)
                acceptedRoleRoster.Remove(player);
        }

        private void ProcessRoleRequest(PlayerRef requester, NetworkPlayerTeam requestedRole)
        {
            if (runner == null || !runner.IsRunning || !runner.IsServer)
                return;

            RoleRequestStatus status = RoleRequestStatus.Rejected;
            RoleRequestRejection rejection = RoleRequestRejection.None;

            if (!IsRoleSelectionOpen(runner))
            {
                rejection = RoleRequestRejection.LobbyClosed;
            }
            else if (!requester.IsValid || !runner.ActivePlayers.Contains(requester))
            {
                rejection = RoleRequestRejection.PlayerNotInSession;
            }
            else if (!IsValidRole(requestedRole))
            {
                rejection = RoleRequestRejection.InvalidRole;
            }
            else
            {
                ReconcileRoleRoster(runner);

                int otherOwners = acceptedRoleRoster.Count(entry =>
                    entry.Key != requester && entry.Value == NetworkPlayerTeam.OrchardOwner);
                int otherThieves = acceptedRoleRoster.Count(entry =>
                    entry.Key != requester && entry.Value == NetworkPlayerTeam.Thief);

                if (requestedRole == NetworkPlayerTeam.OrchardOwner && otherOwners >= 1)
                    rejection = RoleRequestRejection.OwnerSlotTaken;
                else if (requestedRole == NetworkPlayerTeam.Thief && otherThieves >= RequiredThieves)
                    rejection = RoleRequestRejection.ThiefSlotsFull;
                else
                {
                    acceptedRoleRoster[requester] = requestedRole;
                    status = RoleRequestStatus.Accepted;
                }
            }

            if (requester == runner.LocalPlayer)
                roleSelectionMessage = GetRoleMessage(status, rejection, requestedRole);

            SendRoleRosterSnapshot(requester.PlayerId, status, rejection);
            RefreshPlayers(runner);
        }

        private void SendRoleRosterSnapshot(
            int requesterPlayerId,
            RoleRequestStatus status,
            RoleRequestRejection rejection,
            PlayerRef excludedPlayer = default)
        {
            if (runner == null || !runner.IsRunning || !runner.IsServer)
                return;

            PlayerRef[] activePlayers = runner.ActivePlayers
                .Where(player => player != excludedPlayer)
                .OrderBy(player => player.PlayerId)
                .ToArray();
            byte[] payload = new byte[RoleRosterHeaderSize + activePlayers.Length * RoleRosterEntrySize];

            payload[0] = RoleProtocolVersion;
            payload[1] = RoleRosterMessageType;
            payload[2] = (byte)status;
            payload[3] = (byte)rejection;
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, sizeof(int)), requesterPlayerId);
            payload[8] = (byte)activePlayers.Length;

            int offset = RoleRosterHeaderSize;
            foreach (PlayerRef player in activePlayers)
            {
                BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset, sizeof(int)), player.PlayerId);
                payload[offset + sizeof(int)] = acceptedRoleRoster.TryGetValue(player, out NetworkPlayerTeam role)
                    ? (byte)role
                    : UnselectedRoleCode;
                offset += RoleRosterEntrySize;
            }

            foreach (PlayerRef player in activePlayers)
            {
                if (player == runner.LocalPlayer)
                    continue;

                try
                {
                    runner.SendReliableDataToPlayer(player, RoleRosterReliableKey, payload);
                }
                catch (Exception exception)
                {
                    Debug.LogError($"Could not send the authoritative role roster to {player}: {exception.Message}", this);
                }
            }
        }

        private void ApplyRoleRosterSnapshot(NetworkRunner source, PlayerRef sender, ReadOnlySpan<byte> data)
        {
            if (source == null || source != runner || source.IsServer ||
                !IsHostPlayerRef(source, sender) || data.Length < RoleRosterHeaderSize ||
                data[0] != RoleProtocolVersion || data[1] != RoleRosterMessageType ||
                data[2] > (byte)RoleRequestStatus.Rejected ||
                data[3] > (byte)RoleRequestRejection.LobbyClosed)
                return;

            int requesterPlayerId = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4, sizeof(int)));
            int entryCount = data[8];
            if (entryCount > RoomCapacity ||
                data.Length != RoleRosterHeaderSize + entryCount * RoleRosterEntrySize)
                return;

            PlayerRef[] activePlayers = source.ActivePlayers.ToArray();
            if (entryCount != activePlayers.Length)
                return;

            Dictionary<PlayerRef, NetworkPlayerTeam> nextRoster = new();
            HashSet<PlayerRef> seenPlayers = new();
            int ownerCount = 0;
            int thiefCount = 0;
            int offset = RoleRosterHeaderSize;

            for (int index = 0; index < entryCount; index++)
            {
                int playerId = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, sizeof(int)));
                byte roleCode = data[offset + sizeof(int)];
                PlayerRef player = activePlayers.FirstOrDefault(candidate => candidate.PlayerId == playerId);
                if (!player.IsValid || !seenPlayers.Add(player))
                    return;

                if (roleCode == (byte)NetworkPlayerTeam.OrchardOwner)
                {
                    ownerCount++;
                    nextRoster.Add(player, NetworkPlayerTeam.OrchardOwner);
                }
                else if (roleCode == (byte)NetworkPlayerTeam.Thief)
                {
                    thiefCount++;
                    nextRoster.Add(player, NetworkPlayerTeam.Thief);
                }
                else if (roleCode != UnselectedRoleCode)
                {
                    return;
                }

                offset += RoleRosterEntrySize;
            }

            if (ownerCount > RequiredOwners || thiefCount > RequiredThieves)
                return;

            acceptedRoleRoster.Clear();
            foreach (KeyValuePair<PlayerRef, NetworkPlayerTeam> entry in nextRoster)
                acceptedRoleRoster.Add(entry.Key, entry.Value);

            RoleRequestStatus status = (RoleRequestStatus)data[2];
            RoleRequestRejection rejection = (RoleRequestRejection)data[3];
            if (requesterPlayerId == source.LocalPlayer.PlayerId && status != RoleRequestStatus.Snapshot)
            {
                roleRequestPending = false;
                NetworkPlayerTeam requestedRole = LocalAcceptedRole ?? NetworkPlayerTeam.Thief;
                roleSelectionMessage = GetRoleMessage(status, rejection, requestedRole);
            }

            RefreshPlayers(source);
            Changed?.Invoke();
        }

        private static bool IsHostPlayerRef(NetworkRunner source, PlayerRef sender)
        {
            // Fusion assigns the Host's player slot the final PlayerId in Host mode.
            PlayerRef hostPlayer = default;
            int highestPlayerId = int.MinValue;
            foreach (PlayerRef player in source.ActivePlayers)
            {
                if (player.PlayerId > highestPlayerId)
                {
                    highestPlayerId = player.PlayerId;
                    hostPlayer = player;
                }
            }

            return hostPlayer.IsValid && sender == hostPlayer;
        }

        private static bool IsValidRole(NetworkPlayerTeam role) =>
            role == NetworkPlayerTeam.Thief ||
            role == NetworkPlayerTeam.OrchardOwner;

        private static string GetRequiredRosterDescription() =>
            UseTwoPlayerDemo
                ? "assign exactly one OrchardOwner and one Thief player."
                : $"assign exactly one OrchardOwner and {RequiredThieves} Thieves.";

        public static string GetRequiredRosterDescriptionForDiagnostics() => GetRequiredRosterDescription();

        private static string GetRoleMessage(
            RoleRequestStatus status,
            RoleRequestRejection rejection,
            NetworkPlayerTeam requestedRole)
        {
            if (status == RoleRequestStatus.Accepted)
                return $"Host confirmed your role: {requestedRole}.";

            if (status == RoleRequestStatus.Snapshot)
                return UseTwoPlayerDemo
                    ? "Choose OrchardOwner or Thief. The 2-player demo requires one of each."
                    : "Choose a role. The Host must confirm your selection.";

            return rejection switch
            {
                RoleRequestRejection.InvalidMessage => "Role request format was invalid.",
                RoleRequestRejection.InvalidRole => "That role selection is invalid.",
                RoleRequestRejection.PlayerNotInSession => "Your player is no longer in this room.",
                RoleRequestRejection.OwnerSlotTaken => "The OrchardOwner slot is already taken.",
                RoleRequestRejection.ThiefSlotsFull => $"All {RequiredThieves} Thief slot(s) are taken.",
                RoleRequestRejection.LobbyClosed => "Role selection is closed because the match is starting.",
                _ => "The Host rejected the role request."
            };
        }

        private void ClearRoleSelections()
        {
            acceptedRoleRoster.Clear();
            roleRequestPending = false;
            roleSelectionMessage = UseTwoPlayerDemo
                ? "Choose OrchardOwner or Thief. The 2-player demo requires one of each."
                : "Choose a role. The Host must confirm your selection.";
        }

        private void RefreshPlayers(NetworkRunner source)
        {
            if (source == null || source != runner)
                return;

            playerLabels.Clear();
            foreach (PlayerRef player in source.ActivePlayers.OrderBy(player => player.PlayerId))
            {
                string roleText = acceptedRoleRoster.TryGetValue(player, out NetworkPlayerTeam role)
                    ? role.ToString()
                    : "Not selected";
                playerLabels.Add($"Player {player.PlayerId + 1} — {roleText}");
            }
            playerLabels.Sort(StringComparer.Ordinal);
            Changed?.Invoke();
        }

        private void ClearPlayers()
        {
            playerLabels.Clear();
            ClearRoleSelections();
            Changed?.Invoke();
        }

        private void SetState(PhotonLobbyState state, string message)
        {
            State = state;
            StatusMessage = message;
            Changed?.Invoke();
        }

        private static string GenerateRoomCode()
        {
            char[] code = new char[6];
            for (int i = 0; i < code.Length; i++)
                code[i] = CodeAlphabet[UnityEngine.Random.Range(0, CodeAlphabet.Length)];
            return new string(code);
        }

        private static bool IsValidRoomCode(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length < 4 || code.Length > 12)
                return false;
            return code.All(character => CodeAlphabet.IndexOf(character) >= 0);
        }

        private static string FormatStartError(ShutdownReason reason, string detail)
        {
            string result = $"Photon could not start the session ({reason}).";
            if (!string.IsNullOrWhiteSpace(detail))
                result += $" {detail}";
            if (reason == ShutdownReason.GameNotFound)
                result += " Check the room code and make sure the host is still in the room.";
            else if (reason == ShutdownReason.GameIsFull)
                result += " The room has reached its player limit.";
            return result;
        }

        private void ResetMatchReadinessState()
        {
            matchReadyPlayers.Clear();
            matchState = null;
            matchSceneLoadDoneLocally = false;
            matchSceneReadyLocally = false;
            matchSceneSetupPending = false;
            clientSceneObjectAttachDeadline = 0f;
            readinessChallengeSent = false;
            readinessAckSent = false;
            readinessFailureSent = false;
            readinessTimeoutActive = false;
            readinessDeadlineSimulationTime = 0d;
            matchAbortRequested = false;
            returnToLobbyRequested = false;
            returnToLobbyLoadStarted = false;
            lobbySceneLoadDoneLocally = false;
            lobbyReturnChallengeSent = false;
            lobbyReturnAckSent = false;
            clientLobbyReturnInProgress = false;
            clientLobbyReturnAttemptId = 0;
            highestClientLobbyReturnAttemptId = 0;
            lobbyReturnReadyPlayers.Clear();
            lobbyReturnAckTimeoutActive = false;
            lobbyReturnAckDeadlineSimulationTime = 0d;
            lobbySceneLoadDeadlineSimulationTime = 0d;
            matchSceneFailure = null;
            matchAbortReason = null;
            clientReadinessAttemptId = 0;
        }

        private void UpdateMatchLifecycle()
        {
            if (runner == null || !runner.IsRunning)
                return;

            if (!runner.IsServer)
            {
                TrySendClientLobbyReturnAck();
                TrySendClientReadinessResponse();
                return;
            }

            if (returnToLobbyRequested)
            {
                if (!returnToLobbyLoadStarted && SceneManager.GetActiveScene().name == GameSceneName)
                    BeginLobbyReturn();
                else if (returnToLobbyLoadStarted && !lobbySceneLoadDoneLocally &&
                         runner.SimulationTime >= lobbySceneLoadDeadlineSimulationTime)
                    FailLobbyReturn("The synchronized Lobby scene load did not complete before its deadline.");
                else if (lobbySceneLoadDoneLocally)
                    AdvanceLobbyReturnBarrier();
                return;
            }

            if (matchAbortRequested)
            {
                if (SceneManager.GetActiveScene().name == GameSceneName)
                    BeginLobbyReturn();
                return;
            }

            if (!matchSceneReadyLocally || matchState == null || matchState.Object == null ||
                !matchState.Object.IsValid || playerSpawner == null)
                return;

            if (!matchState.HasStateAuthority)
            {
                RequestPrePlayAbort("Host no longer has State Authority over NetworkMatchState.");
                return;
            }

            NetworkMatchPhase phase = matchState.Phase;
            if (phase == NetworkMatchPhase.Aborted || phase == NetworkMatchPhase.Finished)
                return;

            if (phase == NetworkMatchPhase.Playing)
                return;

            if (runner.ActivePlayers.Count() != RequiredPlayers || !HasCompleteRoleRoster(runner))
            {
                RequestPrePlayAbort("A player left before official play could start.");
                return;
            }

            PlayerRef[] activePlayers = runner.ActivePlayers.OrderBy(player => player.PlayerId).ToArray();
            if (!playerSpawner.IsRosterSpawnedAndConfirmed(activePlayers, acceptedRoleRoster))
            {
                if (phase == NetworkMatchPhase.Preparing ||
                    (phase == NetworkMatchPhase.NotStarted && readinessChallengeSent))
                    RequestPrePlayAbort("The complete player roster or confirmed roles were lost before play.");
                return;
            }

            if (!AreInitialMangoesVisible(runner))
            {
                if (phase == NetworkMatchPhase.Preparing ||
                    (phase == NetworkMatchPhase.NotStarted && readinessChallengeSent))
                    RequestPrePlayAbort("The initial network mangoes were lost before official play.");
                return;
            }

            if (phase == NetworkMatchPhase.NotStarted)
            {
                if (!readinessChallengeSent)
                    return;

                if (!AreAllActivePlayersReady(activePlayers))
                {
                    if (readinessTimeoutActive &&
                        runner.SimulationTime >= readinessDeadlineSimulationTime)
                        RequestPrePlayAbort($"Readiness was not confirmed by all players within {MatchReadinessTimeoutSeconds:0} seconds.");
                    return;
                }

                readinessTimeoutActive = false;

                if (!matchState.TryBeginPreparing())
                {
                    RequestPrePlayAbort("Host could not enter the Preparing phase from the current match state.");
                    return;
                }

                StatusMessage = $"All {RequiredPlayers} players are ready. Preparation has started (20 seconds).";
                Changed?.Invoke();
                return;
            }

            if (phase == NetworkMatchPhase.Preparing && matchState.IsPreparationComplete)
            {
                if ((!matchState.IsGameplayReady && !matchState.ConfirmGameplayReady()) ||
                    !matchState.TryStartPlaying())
                {
                    RequestPrePlayAbort("The match could not safely transition from Preparation to Playing.");
                    return;
                }

                StatusMessage = "Playing started. Match timer: 8 minutes.";
                Changed?.Invoke();
            }
        }

        private bool AreAllActivePlayersReady(IReadOnlyCollection<PlayerRef> activePlayers)
        {
            if (activePlayers == null || activePlayers.Count != RequiredPlayers ||
                matchReadyPlayers.Count != RequiredPlayers)
                return false;

            return activePlayers.All(matchReadyPlayers.Contains);
        }

        private bool AreInitialMangoesVisible(NetworkRunner source)
        {
            if (source == null || !source.IsRunning || mangoTrees == null || mangoTrees.Length == 0)
                return false;

            foreach (MangoTree mangoTree in mangoTrees)
            {
                if (mangoTree == null || !mangoTree.AreInitialMangoesVisible(source))
                    return false;
            }

            return true;
        }

        private void BeginReadinessBarrier()
        {
            if (runner == null || !runner.IsRunning || !runner.IsServer || readinessChallengeSent ||
                !matchSceneReadyLocally || playerSpawner == null || matchState == null ||
                matchState.Phase != NetworkMatchPhase.NotStarted)
                return;

            if (matchStartAttemptId <= 0)
            {
                RequestPrePlayAbort("The GameScene load has no valid Host start-attempt identifier.");
                return;
            }

            PlayerRef[] activePlayers = runner.ActivePlayers.OrderBy(player => player.PlayerId).ToArray();
            bool rosterReady = HasCompleteRoleRoster(runner);
            bool playersReady = playerSpawner.IsRosterSpawnedAndConfirmed(activePlayers, acceptedRoleRoster);
            bool mangoesReady = AreInitialMangoesVisible(runner);
            if (!rosterReady || !playersReady || !mangoesReady)
            {
                string reason =
                    $"Host readiness setup failed. ActivePlayers={activePlayers.Length}/{RequiredPlayers}, " +
                    $"AcceptedRoles={acceptedRoleRoster.Count}/{RequiredPlayers}, " +
                    $"RosterReady={rosterReady}, PlayerObjectsReady={playersReady}, " +
                    $"InitialMangoesReady={mangoesReady}, MangoTrees={mangoTrees?.Length ?? 0}.";
                RequestPrePlayAbort(reason);
                return;
            }

            matchReadyPlayers.Clear();
            matchReadyPlayers.Add(runner.LocalPlayer);
            readinessChallengeSent = true;
            readinessDeadlineSimulationTime = runner.SimulationTime + MatchReadinessTimeoutSeconds;
            readinessTimeoutActive = true;

            byte[] challenge = CreateReadinessMessage(MatchReadinessChallengeMessageType, matchStartAttemptId);
            foreach (PlayerRef player in activePlayers)
            {
                if (player == runner.LocalPlayer)
                    continue;

                try
                {
                    runner.SendReliableDataToPlayer(player, MatchReadinessReliableKey, challenge);
                    Debug.Log($"[MatchReadiness][Host] Sent GameScene readiness challenge to {player}. Attempt={matchStartAttemptId}.", this);
                }
                catch (Exception exception)
                {
                    RequestPrePlayAbort($"Could not request GameScene readiness from {player}: {exception.Message}");
                    return;
                }
            }

            StatusMessage = "GameScene loaded. Waiting for all players to confirm scene and spawn readiness.";
            Debug.Log($"[MatchReadiness][Host] Barrier started. LocalPlayer={runner.LocalPlayer}, " +
                      $"Players={string.Join(",", activePlayers.Select(player => player.ToString()))}, " +
                      $"Attempt={matchStartAttemptId}.", this);
            Changed?.Invoke();
        }

        private void TrySendClientReadinessResponse()
        {
            if (clientReadinessAttemptId == 0 || runner == null || !runner.IsRunning || runner.IsServer ||
                !runner.ActivePlayers.Contains(runner.LocalPlayer) || !matchSceneLoadDoneLocally || returnToLobbyRequested ||
                SceneManager.GetActiveScene().name != GameSceneName)
                return;

            if (!string.IsNullOrEmpty(matchSceneFailure))
            {
                if (!readinessFailureSent)
                    SendClientReadinessResponse(MatchReadinessFailedMessageType);
                return;
            }

            if (readinessAckSent || playerSpawner == null || runner == null || !runner.IsRunning ||
                matchState == null || matchState.Object == null || !matchState.Object.IsValid)
                return;

            PlayerRef[] activePlayers = runner.ActivePlayers.OrderBy(player => player.PlayerId).ToArray();
            bool countReady = activePlayers.Length == RequiredPlayers;
            bool playerObjectsReady = countReady &&
                playerSpawner.IsRosterSpawnedAndConfirmed(activePlayers, acceptedRoleRoster);
            bool mangoesReady = AreInitialMangoesVisible(runner);
            if (!countReady || !playerObjectsReady || !mangoesReady)
            {
                if (Time.unscaledTime >= nextClientReadinessDiagnosticTime)
                {
                    nextClientReadinessDiagnosticTime = Time.unscaledTime + 2f;
                    Debug.LogWarning(
                        $"[MatchReadiness][Client] Waiting. ActivePlayers={activePlayers.Length}/{RequiredPlayers}, " +
                        $"AcceptedRoles={acceptedRoleRoster.Count}/{RequiredPlayers}, " +
                        $"PlayerSpawnerReady={playerSpawner != null && playerSpawner.IsInitialized}, " +
                        $"PlayerObjectsReady={playerObjectsReady}, InitialMangoesReady={mangoesReady}, " +
                        $"MangoTrees={mangoTrees?.Length ?? 0}.", this);
                }
                return;
            }

            Debug.Log($"[MatchReadiness][Client] All local readiness checks passed. Sending ACK. " +
                      $"LocalPlayer={runner.LocalPlayer}, Attempt={clientReadinessAttemptId}.", this);
            SendClientReadinessResponse(MatchReadinessAckMessageType);
        }

        private void TrySendClientLobbyReturnAck()
        {
            if (!clientLobbyReturnInProgress || clientLobbyReturnAttemptId <= 0 ||
                lobbyReturnAckSent || runner == null || !runner.IsRunning || runner.IsServer ||
                !lobbySceneLoadDoneLocally || SceneManager.GetActiveScene().name != LobbySceneName ||
                !runner.ActivePlayers.Contains(runner.LocalPlayer))
                return;

            try
            {
                runner.SendReliableDataToServer(
                    MatchReadinessReliableKey,
                    CreateReadinessMessage(LobbyReturnAckMessageType, clientLobbyReturnAttemptId));
                lobbyReturnAckSent = true;
                StatusMessage = "Lobby loaded. Confirmed return to Host.";
                Changed?.Invoke();
            }
            catch (Exception exception)
            {
                StatusMessage = $"Could not confirm Lobby return to Host: {exception.Message}";
                Changed?.Invoke();
            }
        }

        private void BeginLobbyReturnReadinessBarrier()
        {
            if (runner == null || !runner.IsRunning || !runner.IsServer ||
                !returnToLobbyRequested || !lobbySceneLoadDoneLocally || lobbyReturnChallengeSent)
                return;

            if (matchStartAttemptId <= 0)
            {
                FailLobbyReturn("The aborted match has no valid transition attempt identifier.");
                return;
            }

            PlayerRef[] activePlayers = runner.ActivePlayers.OrderBy(player => player.PlayerId).ToArray();
            if (!activePlayers.Contains(runner.LocalPlayer))
            {
                FailLobbyReturn("Host is no longer an active player after returning to Lobby.");
                return;
            }

            lobbyReturnReadyPlayers.Clear();
            lobbyReturnReadyPlayers.Add(runner.LocalPlayer);
            lobbyReturnChallengeSent = true;
            lobbyReturnAckDeadlineSimulationTime = runner.SimulationTime + LobbyReturnAckTimeoutSeconds;
            lobbyReturnAckTimeoutActive = true;

            byte[] challenge = CreateReadinessMessage(LobbyReturnChallengeMessageType, matchStartAttemptId);
            foreach (PlayerRef player in activePlayers)
            {
                if (player == runner.LocalPlayer)
                    continue;

                try
                {
                    runner.SendReliableDataToPlayer(player, MatchReadinessReliableKey, challenge);
                }
                catch (Exception exception)
                {
                    FailLobbyReturn($"Could not request Lobby confirmation from {player}: {exception.Message}");
                    return;
                }
            }

            StatusMessage = "Lobby loaded locally. Confirming the return with all remaining players.";
            Changed?.Invoke();
        }

        private void AdvanceLobbyReturnBarrier()
        {
            if (!returnToLobbyRequested || !lobbySceneLoadDoneLocally || !lobbyReturnChallengeSent ||
                !lobbyReturnAckTimeoutActive || runner == null || !runner.IsRunning || !runner.IsServer)
                return;

            PlayerRef[] activePlayers = runner.ActivePlayers.OrderBy(player => player.PlayerId).ToArray();
            if (activePlayers.All(lobbyReturnReadyPlayers.Contains) &&
                lobbyReturnReadyPlayers.Count == activePlayers.Length)
            {
                byte[] complete = CreateReadinessMessage(LobbyReturnCompleteMessageType, matchStartAttemptId);
                foreach (PlayerRef player in activePlayers)
                {
                    if (player == runner.LocalPlayer)
                        continue;

                    try
                    {
                        runner.SendReliableDataToPlayer(player, MatchReadinessReliableKey, complete);
                    }
                    catch (Exception exception)
                    {
                        FailLobbyReturn($"Could not confirm Lobby return to {player}: {exception.Message}");
                        return;
                    }
                }

                CompleteHostLobbyReturn();
                return;
            }

            if (runner.SimulationTime >= lobbyReturnAckDeadlineSimulationTime)
                FailLobbyReturn($"Not all remaining peers confirmed Lobby within {LobbyReturnAckTimeoutSeconds:0} seconds.");
        }

        private void CompleteHostLobbyReturn()
        {
            startGameRequested = false;
            matchAbortRequested = false;
            returnToLobbyRequested = false;
            returnToLobbyLoadStarted = false;
            lobbySceneLoadDoneLocally = false;
            lobbyReturnChallengeSent = false;
            lobbyReturnAckTimeoutActive = false;
            readinessChallengeSent = false;
            readinessTimeoutActive = false;
            matchReadyPlayers.Clear();
            lobbyReturnReadyPlayers.Clear();
            ReconcileRoleRoster(runner);
            RefreshPlayers(runner);
            StatusMessage = string.IsNullOrEmpty(matchAbortReason)
                ? "Returned to Lobby before the match started."
                : $"Returned to Lobby. {matchAbortReason}";
            Changed?.Invoke();
        }

        private void CompleteClientLobbyReturn(int attemptId)
        {
            if (attemptId != clientLobbyReturnAttemptId || !clientLobbyReturnInProgress)
                return;

            clientLobbyReturnInProgress = false;
            lobbyReturnAckSent = false;
            startGameRequested = false;
            StatusMessage = "Host confirmed the session returned to Lobby.";
            Changed?.Invoke();
        }

        private void SendClientReadinessResponse(byte messageType)
        {
            if (runner == null || !runner.IsRunning || runner.IsServer || clientReadinessAttemptId == 0)
                return;

            try
            {
                byte[] response;
                if (messageType == MatchReadinessFailedMessageType)
                {
                    // Include the actual client-side scene/setup failure so the Host can report
                    // the root cause without requiring access to the Client's separate Console.
                    byte[] reasonBytes = Encoding.UTF8.GetBytes(
                        string.IsNullOrWhiteSpace(matchSceneFailure)
                            ? "Client reported an unspecified GameScene setup failure."
                            : matchSceneFailure);

                    const int headerSize = 2 + sizeof(int);
                    response = new byte[headerSize + sizeof(ushort) + reasonBytes.Length];
                    response[0] = RoleProtocolVersion;
                    response[1] = messageType;
                    BinaryPrimitives.WriteInt32LittleEndian(
                        response.AsSpan(2, sizeof(int)), clientReadinessAttemptId);
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        response.AsSpan(headerSize, sizeof(ushort)), (ushort)reasonBytes.Length);
                    reasonBytes.CopyTo(response, headerSize + sizeof(ushort));
                }
                else
                {
                    response = CreateReadinessMessage(messageType, clientReadinessAttemptId);
                }

                runner.SendReliableDataToServer(MatchReadinessReliableKey, response);
                if (messageType == MatchReadinessAckMessageType)
                {
                    readinessAckSent = true;
                    Debug.Log($"[MatchReadiness][Client] ACK sent to Host. LocalPlayer={runner.LocalPlayer}, Attempt={clientReadinessAttemptId}.", this);
                }
                else
                {
                    readinessFailureSent = true;
                    Debug.LogError($"[MatchReadiness][Client] FAILURE sent to Host. SceneFailure={matchSceneFailure}", this);
                }
            }
            catch (Exception exception)
            {
                StatusMessage = $"Could not report GameScene readiness to Host: {exception.Message}";
                Changed?.Invoke();
            }
        }

        private static byte[] CreateReadinessMessage(byte messageType, int attemptId)
        {
            byte[] message = new byte[2 + sizeof(int)];
            message[0] = RoleProtocolVersion;
            message[1] = messageType;
            BinaryPrimitives.WriteInt32LittleEndian(message.AsSpan(2, sizeof(int)), attemptId);
            return message;
        }

        private void HandleMatchReadinessMessage(NetworkRunner source, PlayerRef sender, ReadOnlySpan<byte> data)
        {
            if (source != runner || !source.IsRunning || !source.IsServer ||
                !sender.IsValid || !source.ActivePlayers.Contains(sender) ||
                data.Length < 2 + sizeof(int) || data[0] != RoleProtocolVersion)
                return;

            byte messageType = data[1];
            int attemptId = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(2, sizeof(int)));

            if (messageType == LobbyReturnAckMessageType)
            {
                if (data.Length != 2 + sizeof(int))
                    return;

                if (SceneManager.GetActiveScene().name != LobbySceneName ||
                    !returnToLobbyRequested || !lobbySceneLoadDoneLocally ||
                    !lobbyReturnChallengeSent || attemptId != matchStartAttemptId ||
                    !lobbyReturnAckTimeoutActive)
                    return;

                lobbyReturnReadyPlayers.Add(sender);
                return;
            }

            if (SceneManager.GetActiveScene().name != GameSceneName ||
                !acceptedRoleRoster.ContainsKey(sender) || !readinessChallengeSent ||
                !readinessTimeoutActive || matchAbortRequested || returnToLobbyRequested)
                return;

            if (attemptId != matchStartAttemptId || matchState == null ||
                matchState.Phase != NetworkMatchPhase.NotStarted)
                return;

            if (messageType == MatchReadinessFailedMessageType)
            {
                string clientFailure = "Client did not include a detailed setup error.";
                const int headerSize = 2 + sizeof(int);
                if (data.Length >= headerSize + sizeof(ushort))
                {
                    int reasonLength = BinaryPrimitives.ReadUInt16LittleEndian(
                        data.Slice(headerSize, sizeof(ushort)));
                    if (reasonLength != data.Length - headerSize - sizeof(ushort))
                        return;

                    clientFailure = Encoding.UTF8.GetString(
                        data.Slice(headerSize + sizeof(ushort), reasonLength));
                }
                else if (data.Length != headerSize)
                {
                    return;
                }

                Debug.LogError(
                    $"[MatchReadiness][Host] Client {sender} reported setup failure: {clientFailure}", this);
                RequestPrePlayAbort(
                    $"Client {sender} reported GameScene setup failure: {clientFailure}");
                return;
            }

            if (messageType != MatchReadinessAckMessageType || data.Length != 2 + sizeof(int))
                return;

            PlayerRef[] activePlayers = source.ActivePlayers.OrderBy(player => player.PlayerId).ToArray();
            if (!HasCompleteRoleRoster(source) || playerSpawner == null ||
                !playerSpawner.IsRosterSpawnedAndConfirmed(activePlayers, acceptedRoleRoster))
            {
                RequestPrePlayAbort("Host could not verify the roster while processing readiness acknowledgements.");
                return;
            }

            matchReadyPlayers.Add(sender);
            Debug.Log($"[MatchReadiness][Host] Accepted ACK from {sender}. " +
                      $"Ready={matchReadyPlayers.Count}/{RequiredPlayers}, Attempt={attemptId}.", this);
        }

        private void HandleReadinessChallenge(NetworkRunner source, PlayerRef sender, ReadOnlySpan<byte> data)
        {
            if (source != runner || !source.IsRunning || source.IsServer ||
                !source.ActivePlayers.Contains(source.LocalPlayer) || !IsHostPlayerRef(source, sender) ||
                data.Length != 2 + sizeof(int) || data[0] != RoleProtocolVersion)
                return;

            int attemptId = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(2, sizeof(int)));
            if (attemptId <= 0)
                return;

            if (data[1] == MatchReadinessChallengeMessageType)
            {
                // A stale or duplicate challenge cannot reopen a canceled/completed attempt.
                if (attemptId <= highestClientReadinessAttemptId)
                    return;

                highestClientReadinessAttemptId = attemptId;
                clientReadinessAttemptId = attemptId;
                readinessAckSent = false;
                readinessFailureSent = false;
                Debug.Log($"[MatchReadiness][Client] Received Host readiness challenge. " +
                          $"Host={sender}, LocalPlayer={source.LocalPlayer}, Attempt={attemptId}.", this);
                TrySendClientReadinessResponse();
                return;
            }

            if (data[1] != LobbyReturnChallengeMessageType ||
                attemptId <= highestClientLobbyReturnAttemptId)
            {
                if (data[1] == LobbyReturnCompleteMessageType && clientLobbyReturnInProgress &&
                    attemptId == clientLobbyReturnAttemptId)
                    CompleteClientLobbyReturn(attemptId);
                return;
            }

            highestClientLobbyReturnAttemptId = attemptId;
            clientLobbyReturnAttemptId = attemptId;
            clientLobbyReturnInProgress = true;
            Changed?.Invoke();
            TrySendClientLobbyReturnAck();
        }

        private void RequestPrePlayAbort(string reason)
        {
            if (runner == null || !runner.IsRunning || !runner.IsServer ||
                returnToLobbyRequested || matchAbortRequested)
                return;

            if (matchState != null &&
                (matchState.Phase == NetworkMatchPhase.Playing || matchState.Phase == NetworkMatchPhase.Finished))
                return;

            matchAbortRequested = true;
            readinessTimeoutActive = false;
            matchAbortReason = reason;
            StatusMessage = $"Match start canceled: {reason}";
            Debug.LogError($"[MatchLifecycle][Host] PRE-PLAY ABORT: {reason}", this);
            Changed?.Invoke();
        }

        private void BeginLobbyReturn()
        {
            if (runner == null || !runner.IsRunning || !runner.IsServer || returnToLobbyLoadStarted)
                return;

            if (matchState != null &&
                (matchState.Phase == NetworkMatchPhase.Playing || matchState.Phase == NetworkMatchPhase.Finished))
            {
                matchAbortRequested = false;
                return;
            }

            returnToLobbyRequested = true;
            returnToLobbyLoadStarted = true;
            Debug.LogWarning($"[MatchLifecycle][Host] Returning from GameScene to Lobby. AbortReason=" +
                             $"{(string.IsNullOrEmpty(matchAbortReason) ? "<none recorded>" : matchAbortReason)}", this);
            readinessTimeoutActive = false;
            lobbySceneLoadDoneLocally = false;
            lobbyReturnChallengeSent = false;
            lobbyReturnAckTimeoutActive = false;
            lobbyReturnReadyPlayers.Clear();

            // The PlayerSpawner owns the runtime player objects; clear them before unloading GameScene.
            if (playerSpawner != null)
                playerSpawner.DespawnAllPlayers();

            if (mangoTrees != null)
            {
                foreach (MangoTree mangoTree in mangoTrees)
                {
                    if (mangoTree != null)
                        mangoTree.DespawnInitialMangoes(runner);
                }
            }

            if (matchState != null && matchState.Object != null && matchState.Object.IsValid &&
                matchState.HasStateAuthority && !matchState.TryAbortBeforePlaying() &&
                matchState.Phase != NetworkMatchPhase.Aborted)
            {
                StatusMessage = "Could not mark the pre-play match as aborted; returning to Lobby without resetting it.";
            }

            int lobbyBuildIndex = SceneUtility.GetBuildIndexByScenePath(LobbyScenePath);
            if (lobbyBuildIndex < 0)
            {
                FailLobbyReturn("LobbyScene is not enabled in Build Settings.");
                return;
            }

            try
            {
                NetworkSceneAsyncOp loadOperation = runner.LoadScene(
                    SceneRef.FromIndex(lobbyBuildIndex), LoadSceneMode.Single);
                if (!loadOperation.IsValid)
                {
                    FailLobbyReturn($"Fusion rejected the Lobby transition: {loadOperation.Error}");
                    return;
                }

                lobbySceneLoadDeadlineSimulationTime = runner.SimulationTime + LobbyReturnLoadTimeoutSeconds;
                _ = ObserveLobbyReturnAsync(runner, loadOperation);
            }
            catch (Exception exception)
            {
                FailLobbyReturn($"Could not return to Lobby: {exception.Message}");
            }
        }

        private async Task ObserveLobbyReturnAsync(NetworkRunner source, NetworkSceneAsyncOp loadOperation)
        {
            while (!loadOperation.IsDone)
            {
                if (source == null || source != runner || !source.IsRunning)
                    return;
                await Task.Yield();
            }

            if (source != runner || loadOperation.Error == null)
                return;

            FailLobbyReturn($"The synchronized Lobby load failed: {loadOperation.Error}");
        }

        private void FailLobbyReturn(string reason)
        {
            if (!returnToLobbyRequested || leaveRequested)
                return;

            returnToLobbyRequested = false;
            returnToLobbyLoadStarted = false;
            matchAbortRequested = false;
            lobbyReturnAckTimeoutActive = false;
            StatusMessage = $"{reason} Closing the Photon session and returning to Menu.";
            Debug.LogError($"[MatchLifecycle][Host] LOBBY RETURN FAILED: {reason}. Session will close and return to Menu.", this);
            Changed?.Invoke();
            _ = LeaveAndReturnToMenuAsync();
        }

        public void OnPlayerJoined(NetworkRunner currentRunner, PlayerRef player)
        {
            if (currentRunner != runner)
                return;

            if (currentRunner.IsServer && !startGameRequested &&
                SceneManager.GetActiveScene().name == LobbySceneName)
            {
                ReconcileRoleRoster(currentRunner);
                SendRoleRosterSnapshot(-1, RoleRequestStatus.Snapshot, RoleRequestRejection.None);
            }

            if (playerSpawner != null && currentRunner.IsServer)
            {
                if (acceptedRoleRoster.TryGetValue(player, out NetworkPlayerTeam role))
                    playerSpawner.HandlePlayerJoined(player, role);
                else
                    playerSpawner.HandlePlayerJoined(player, null);
            }

            if (currentRunner.IsServer && !returnToLobbyRequested &&
                SceneManager.GetActiveScene().name == GameSceneName &&
                matchState != null && matchState.Phase != NetworkMatchPhase.Playing &&
                matchState.Phase != NetworkMatchPhase.Finished)
            {
                RequestPrePlayAbort("A new player attempted to join after the Lobby was closed.");
            }

            RefreshPlayers(currentRunner);
        }

        public void OnPlayerLeft(NetworkRunner currentRunner, PlayerRef player)
        {
            if (currentRunner != runner)
                return;

            if (playerSpawner != null)
                playerSpawner.HandlePlayerLeft(player);

            if (currentRunner.IsServer)
            {
                lobbyReturnReadyPlayers.Remove(player);
                acceptedRoleRoster.Remove(player);
                ReconcileRoleRoster(currentRunner);
                SendRoleRosterSnapshot(-1, RoleRequestStatus.Snapshot, RoleRequestRejection.None, player);

                NetworkMatchPhase phase = matchState != null
                    ? matchState.Phase
                    : NetworkMatchPhase.NotStarted;
                if ((startGameRequested || SceneManager.GetActiveScene().name == GameSceneName) &&
                    phase != NetworkMatchPhase.Playing && phase != NetworkMatchPhase.Finished)
                {
                    RequestPrePlayAbort("A player left before official play began.");
                }
            }
            RefreshPlayers(currentRunner);
        }
        public void OnShutdown(NetworkRunner currentRunner, ShutdownReason shutdownReason)
        {
            if (currentRunner == runner && !shuttingDown)
                HandleUnexpectedEnd(currentRunner, $"Photon session ended: {shutdownReason}.");
        }

        public void OnDisconnectedFromServer(NetworkRunner currentRunner, NetDisconnectReason reason) =>
            HandleUnexpectedEnd(currentRunner, $"Disconnected from Photon: {reason}.");

        public void OnConnectFailed(NetworkRunner currentRunner, NetAddress remoteAddress, NetConnectFailedReason reason) =>
            HandleUnexpectedEnd(currentRunner, $"Photon connection failed: {reason}.");

        public void OnConnectedToServer(NetworkRunner currentRunner)
        {
            if (currentRunner == runner)
                Changed?.Invoke();
        }
        public void OnInput(NetworkRunner currentRunner, NetworkInput input)
        {
            if (currentRunner != runner)
                return;

            NetworkButtons buttons = default;
            buttons.Set(PlayerInputButton.Interact, pendingInteract);
            buttons.Set(PlayerInputButton.Harvest, pendingHarvest);
            buttons.Set(PlayerInputButton.Jump, pendingJump);
            buttons.Set(PlayerInputButton.DropMango, pendingDropMango);
            buttons.Set(PlayerInputButton.Sprint, sprintHeld);
            buttons.Set(PlayerInputButton.Crouch, crouchHeld);
            buttons.Set(PlayerInputButton.Rescue, rescueHeld);
            buttons.Set(PlayerInputButton.DebugGiveMango, pendingDebugGiveMango);
            buttons.Set(PlayerInputButton.GardenTrap, pendingGardenTrap);
            buttons.Set(PlayerInputButton.GardenDog, pendingGardenDog);
            buttons.Set(PlayerInputButton.GardenSlipper, pendingGardenSlipper);

            input.Set(new NetworkPlayerInput
            {
                Move = cachedMove,
                Buttons = buttons,
                CameraYaw = LocalPlayerCameraFollow.CurrentYaw
            });

            // Edge-triggered input remains queued until Fusion consumes an input tick.
            pendingInteract = false;
            pendingHarvest = false;
            pendingJump = false;
            pendingDropMango = false;
            pendingDebugGiveMango = false;
            pendingGardenTrap = false;
            pendingGardenDog = false;
            pendingGardenSlipper = false;
        }
        public void OnInputMissing(NetworkRunner currentRunner, PlayerRef player, NetworkInput input) { }
        public void OnConnectRequest(NetworkRunner currentRunner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
        {
            if (currentRunner != runner || !currentRunner.IsServer || !currentRunner.IsRunning ||
                startGameRequested || returnToLobbyRequested || matchAbortRequested ||
                SceneManager.GetActiveScene().name == GameSceneName)
            {
                request.Refuse();
                return;
            }

            request.Accept();
        }
        public void OnUserSimulationMessage(NetworkRunner currentRunner, SimulationMessagePtr message) { }
        public void OnSessionListUpdated(NetworkRunner currentRunner, List<SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner currentRunner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner currentRunner, HostMigrationToken hostMigrationToken)
        {
            if (currentRunner == runner)
                HandleUnexpectedEnd(currentRunner,
                    "Photon Host migration is not supported for this MVP session.");
        }
        public void OnReliableDataReceived(NetworkRunner currentRunner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data)
        {
            if (currentRunner != runner || !currentRunner.IsRunning)
                return;

            if (currentRunner.IsServer)
            {
                if (key != RoleRequestReliableKey)
                {
                    if (key == MatchReadinessReliableKey)
                        HandleMatchReadinessMessage(currentRunner, player, data);
                    return;
                }

                HandleReliableRoleRequest(currentRunner, player, data);
                return;
            }

            if (key == RoleRosterReliableKey)
                ApplyRoleRosterSnapshot(currentRunner, player, data);
            else if (key == MatchReadinessReliableKey)
                HandleReadinessChallenge(currentRunner, player, data);
        }

        private void HandleReliableRoleRequest(NetworkRunner currentRunner, PlayerRef requester, ReadOnlySpan<byte> data)
        {
            if (!requester.IsValid || !currentRunner.ActivePlayers.Contains(requester))
                return;

            if (data.Length != 3 || data[0] != RoleProtocolVersion || data[1] != RoleRequestMessageType)
            {
                SendRoleRosterSnapshot(requester.PlayerId, RoleRequestStatus.Rejected,
                    RoleRequestRejection.InvalidMessage);
                return;
            }

            byte roleCode = data[2];
            if (roleCode != (byte)NetworkPlayerTeam.Thief &&
                roleCode != (byte)NetworkPlayerTeam.OrchardOwner)
            {
                SendRoleRosterSnapshot(requester.PlayerId, RoleRequestStatus.Rejected,
                    RoleRequestRejection.InvalidRole);
                return;
            }

            ProcessRoleRequest(requester, (NetworkPlayerTeam)roleCode);
        }
        public void OnReliableDataProgress(NetworkRunner currentRunner, PlayerRef player, ReliableKey key, float progress) { }
        private void FailGameSceneSetup(NetworkRunner currentRunner, string reason)
        {
            matchSceneSetupPending = false;
            matchSceneReadyLocally = false;
            matchSceneFailure = string.IsNullOrWhiteSpace(reason)
                ? "GameScene setup failed for an unspecified reason."
                : reason;

            StatusMessage = matchSceneFailure;
            Debug.LogError($"[GameSceneSetup][{(currentRunner.IsServer ? "Host" : "Client")}] {matchSceneFailure}", this);

            // Only Host can abort the shared match. A Client reports its failure through
            // the existing detailed Reliable Data readiness response.
            if (currentRunner.IsServer)
                RequestPrePlayAbort(matchSceneFailure);
        }

        private void TryCompletePendingGameSceneSetup()
        {
            if (!matchSceneSetupPending || runner == null || !runner.IsRunning || runner.IsServer ||
                SceneManager.GetActiveScene().name != GameSceneName)
                return;

            bool attached = matchState != null &&
                            matchState.Object != null &&
                            matchState.Object.IsValid;

            if (attached)
            {
                Debug.Log(
                    $"[GameSceneSetup][Client] NetworkMatchState attached. " +
                    $"ObjectValid={matchState.Object.IsValid}. Completing local scene setup.", this);
                matchSceneSetupPending = false;
                CompleteGameSceneSetup(runner);
                return;
            }

            if (Time.unscaledTime >= clientSceneObjectAttachDeadline)
            {
                string objectInfo = matchState == null
                    ? "ComponentMissing=true"
                    : $"ComponentFound=true, ObjectExists={(matchState.Object != null)}, " +
                      $"ObjectValid={(matchState.Object != null && matchState.Object.IsValid)}, " +
                      $"GameObjectActive={matchState.gameObject.activeInHierarchy}, " +
                      $"ComponentEnabled={matchState.enabled}";

                FailGameSceneSetup(
                    runner,
                    $"Client could not attach the GameScene NetworkMatchState within " +
                    $"{ClientSceneObjectAttachTimeoutSeconds:0} seconds. {objectInfo}. " +
                    "Check that Host and Client use the same GameScene asset and baked Fusion scene-object data.");
                return;
            }

            if (Time.unscaledTime >= nextClientReadinessDiagnosticTime)
            {
                nextClientReadinessDiagnosticTime = Time.unscaledTime + 2f;
                Debug.LogWarning(
                    "[GameSceneSetup][Client] Waiting for Fusion to attach NetworkMatchState. " +
                    $"ComponentFound={(matchState != null)}, " +
                    $"ObjectExists={(matchState != null && matchState.Object != null)}, " +
                    $"ObjectValid={(matchState != null && matchState.Object != null && matchState.Object.IsValid)}, " +
                    $"Remaining={Mathf.Max(0f, clientSceneObjectAttachDeadline - Time.unscaledTime):0.0}s",
                    this);
            }
        }

        private void CompleteGameSceneSetup(NetworkRunner currentRunner)
        {
            if (currentRunner == null || currentRunner != runner || !currentRunner.IsRunning ||
                SceneManager.GetActiveScene().name != GameSceneName || matchSceneSetupPending)
                return;

            if (matchState == null)
            {
                FailGameSceneSetup(currentRunner, "GameScene does not contain NetworkMatchState.");
                return;
            }

            if (matchState.Object == null || !matchState.Object.IsValid)
            {
                FailGameSceneSetup(currentRunner,
                    "NetworkMatchState was found, but its Fusion NetworkObject is not attached.");
                return;
            }

            if (currentRunner.IsServer && !matchState.HasStateAuthority)
            {
                FailGameSceneSetup(currentRunner,
                    "Host does not have State Authority over NetworkMatchState.");
                return;
            }

            PlayerSpawner[] spawners = FindObjectsByType<PlayerSpawner>(FindObjectsSortMode.None);
            if (spawners.Length != 1)
            {
                FailGameSceneSetup(currentRunner,
                    $"Expected exactly one PlayerSpawner in GameScene, found {spawners.Length}.");
                return;
            }
            playerSpawner = spawners[0];

            mangoTrees = FindObjectsByType<MangoTree>(FindObjectsSortMode.None);
            if (mangoTrees.Length == 0)
            {
                FailGameSceneSetup(currentRunner, "GameScene has no MangoTree spawn points.");
                return;
            }

            foreach (MangoTree mangoTree in mangoTrees)
            {
                if (mangoTree == null || !mangoTree.HasConfiguredMangoPoints)
                {
                    FailGameSceneSetup(currentRunner,
                        "GameScene contains a MangoTree with missing mango spawn points.");
                    return;
                }
            }

            if (!playerSpawner.Initialize(currentRunner, acceptedRoleRoster))
            {
                FailGameSceneSetup(currentRunner,
                    string.IsNullOrWhiteSpace(playerSpawner.InitializationError)
                        ? "PlayerSpawner could not initialize."
                        : playerSpawner.InitializationError);
                return;
            }

            if (currentRunner.IsServer)
            {
                foreach (MangoTree mangoTree in mangoTrees)
                {
                    if (mangoTree.EnsureInitialMangoesSpawned(currentRunner, out string mangoError))
                        continue;

                    FailGameSceneSetup(currentRunner, mangoError);
                    return;
                }
            }

            matchSceneFailure = null;
            matchSceneReadyLocally = true;
            StatusMessage = "GameScene loaded. Synchronizing player readiness.";
            Debug.Log(
                $"[GameSceneSetup][{(currentRunner.IsServer ? "Host" : "Client")}] " +
                "Scene objects, PlayerSpawner and MangoTree setup completed.", this);

            if (currentRunner.IsServer)
                BeginReadinessBarrier();
        }

        public void OnSceneLoadDone(NetworkRunner currentRunner)
        {
            if (currentRunner != runner)
                return;

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.name == GameSceneName)
            {
                matchSceneLoadDoneLocally = true;
                matchSceneReadyLocally = false;
                matchSceneFailure = null;
                matchSceneSetupPending = false;
                playerSpawner = null;
                mangoTrees = Array.Empty<MangoTree>();
                matchState = null;

                Debug.Log(
                    $"[GameSceneAudit] Peer={(currentRunner.IsServer ? "HOST" : "CLIENT")} | " +
                    $"Scene={activeScene.name} | Path={activeScene.path} | " +
                    $"BuildIndex={activeScene.buildIndex} | Loaded={activeScene.isLoaded}",
                    this);

                Assets.Scripts.GameManager.NetworkMatchState[] allMatchStates =
                    FindObjectsByType<Assets.Scripts.GameManager.NetworkMatchState>(
                        FindObjectsInactive.Include, FindObjectsSortMode.None);

                Assets.Scripts.GameManager.NetworkMatchState[] matchStates = allMatchStates
                    .Where(state => state != null && state.gameObject.scene == activeScene)
                    .ToArray();

                Debug.Log(
                    $"[GameSceneAudit] Peer={(currentRunner.IsServer ? "HOST" : "CLIENT")} | " +
                    $"AllStates={allMatchStates.Length} | StatesInActiveScene={matchStates.Length}",
                    this);

                foreach (Assets.Scripts.GameManager.NetworkMatchState state in matchStates)
                {
                    Debug.Log(
                        $"[GameSceneAudit] Object={state.gameObject.name} | " +
                        $"Active={state.gameObject.activeInHierarchy} | " +
                        $"ComponentEnabled={state.enabled} | " +
                        $"ObjectExists={(state.Object != null)} | " +
                        $"NetworkObjectValid={(state.Object != null && state.Object.IsValid)}",
                        state);
                }

                if (matchStates.Length != 1)
                {
                    FailGameSceneSetup(currentRunner,
                        $"Expected exactly one NetworkMatchState in active GameScene, found {matchStates.Length}.");
                }
                else
                {
                    matchState = matchStates[0];
                    bool objectAttached = matchState.Object != null && matchState.Object.IsValid;

                    if (!objectAttached && !currentRunner.IsServer)
                    {
                        // Scene load callback means Unity loaded the scene, but a Client can
                        // still be waiting for Fusion to attach the Host-assigned NetworkId.
                        // Keep the scene setup pending; do not send a premature Failure.
                        matchSceneSetupPending = true;
                        clientSceneObjectAttachDeadline =
                            Time.unscaledTime + ClientSceneObjectAttachTimeoutSeconds;

                        Debug.LogWarning(
                            "[GameSceneSetup][Client] NetworkMatchState was found in GameScene, " +
                            "but Fusion has not attached it yet. Waiting before reporting setup failure.",
                            this);
                    }
                    else
                    {
                        CompleteGameSceneSetup(currentRunner);
                    }
                }
            }
            else
            {
                playerSpawner = null;
                mangoTrees = Array.Empty<MangoTree>();
                matchState = null;
                matchSceneLoadDoneLocally = false;
                matchSceneReadyLocally = false;
                matchSceneSetupPending = false;
                clientSceneObjectAttachDeadline = 0f;

                if (activeScene.name == LobbySceneName)
                {
                    lobbySceneLoadDoneLocally = true;
                    if (currentRunner.IsServer && returnToLobbyRequested)
                        BeginLobbyReturnReadinessBarrier();
                    else
                        TrySendClientLobbyReturnAck();
                }
            }

            Changed?.Invoke();
        }

        public void OnSceneLoadStart(NetworkRunner currentRunner)
        {
            if (currentRunner == runner)
                Changed?.Invoke();
        }
        public void OnObjectExitAOI(NetworkRunner currentRunner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner currentRunner, NetworkObject obj, PlayerRef player) { }
    }
}
