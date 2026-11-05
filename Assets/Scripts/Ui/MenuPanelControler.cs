
using MangoHeist.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MangoHeist.UI
{
    /// <summary>
    /// Controls existing MenuScene panels and connects
    /// Host/Join buttons to PhotonLobbyManager.
    /// This component does not create UI or implement networking.
    /// </summary>
    public sealed class MenuPanelController : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private GameObject mainPanel;
        [SerializeField] private GameObject hostGamePanel;
        [SerializeField] private GameObject joinLobbyPanel;
        [SerializeField] private GameObject playOfflinePanel;
        [SerializeField] private GameObject howToPlayPanel;
        [SerializeField] private GameObject optionsPanel;
        [SerializeField] private GameObject quitConfirmPanel;

        [Header("How To Play")]
        [SerializeField] private GameObject thiefGuideContent;
        [SerializeField] private GameObject ownerGuideContent;

        [Header("Host Game Fields")]
        [SerializeField] private TMP_InputField hostRoomCodeInput;
        [SerializeField] private TMP_Text hostRoomInfoText;
        [SerializeField] private TMP_Text hostStatusText;
        [SerializeField] private Button createRoomButton;
        [SerializeField] private Button hostBackToMenuButton;

        [Header("Join Lobby Fields")]
        [SerializeField] private TMP_InputField joinRoomCodeInput;
        [SerializeField] private TMP_Text joinStatusText;
        [SerializeField] private Button joinRoomButton;
        [SerializeField] private Button joinBackToMenuButton;

        [Header("Play Offline Fields")]
        [SerializeField] private Button offlineBackToMenuButton;

        private PhotonLobbyManager lobbyManager;

        private void Awake()
        {
            lobbyManager = PhotonLobbyManager.EnsureInstance();

            SetHostRoomInfo();
            ShowMainPanel();
        }

        private void OnEnable()
        {
            if (lobbyManager == null)
                lobbyManager = PhotonLobbyManager.EnsureInstance();

            if (lobbyManager != null)
                lobbyManager.Changed += RefreshConnectionStatus;

            RegisterButtonListeners();
            RefreshConnectionStatus();
        }

        private void OnDisable()
        {
            if (lobbyManager != null)
                lobbyManager.Changed -= RefreshConnectionStatus;

            RemoveButtonListeners();
        }

        private void RegisterButtonListeners()
        {
            if (createRoomButton != null)
                createRoomButton.onClick.AddListener(CreateRoom);

            if (joinRoomButton != null)
                joinRoomButton.onClick.AddListener(JoinRoom);

            if (hostBackToMenuButton != null)
                hostBackToMenuButton.onClick.AddListener(BackToMainMenu);

            if (joinBackToMenuButton != null)
                joinBackToMenuButton.onClick.AddListener(BackToMainMenu);

            if (offlineBackToMenuButton != null)
                offlineBackToMenuButton.onClick.AddListener(BackToMainMenu);
        }

        private void RemoveButtonListeners()
        {
            if (createRoomButton != null)
                createRoomButton.onClick.RemoveListener(CreateRoom);

            if (joinRoomButton != null)
                joinRoomButton.onClick.RemoveListener(JoinRoom);

            if (hostBackToMenuButton != null)
                hostBackToMenuButton.onClick.RemoveListener(BackToMainMenu);

            if (joinBackToMenuButton != null)
                joinBackToMenuButton.onClick.RemoveListener(BackToMainMenu);

            if (offlineBackToMenuButton != null)
                offlineBackToMenuButton.onClick.RemoveListener(BackToMainMenu);
        }

        private void SetHostRoomInfo()
        {
            if (hostRoomInfoText == null)
                return;

            hostRoomInfoText.text = PhotonLobbyManager.UseTwoPlayerDemo
                ? "Map: Mango Orchard\nMaximum players: 2\nMode: 1 OrchardOwner + 1 Thief"
                : "Map: Mango Orchard\nMaximum players: 5\nMode: 1 OrchardOwner + 4 Thieves";
        }

        // Main Menu navigation

        public void ShowMainPanel()
        {
            ShowOnly(null);
        }

        public void OpenHostGamePanel()
        {
            if (hostRoomCodeInput != null)
                hostRoomCodeInput.text = string.Empty;

            SetStatus(hostStatusText, string.Empty);
            ShowOnly(hostGamePanel);
            RefreshConnectionStatus();
        }

        public void OpenJoinLobbyPanel()
        {
            if (joinRoomCodeInput != null)
                joinRoomCodeInput.text = string.Empty;

            SetStatus(joinStatusText, string.Empty);
            ShowOnly(joinLobbyPanel);
            RefreshConnectionStatus();
        }

        public void OpenPlayOfflinePanel()
        {
            ShowOnly(playOfflinePanel);
        }

        public void OpenHowToPlayPanel()
        {
            ShowOnly(howToPlayPanel);
            ShowThiefGuide();
        }

        public void OpenOptionsPanel()
        {
            ShowOnly(optionsPanel);
        }

        public void OpenQuitConfirmPanel()
        {
            ShowOnly(quitConfirmPanel);
        }

        // Host Game

        public void CreateRoom()
        {
            if (lobbyManager == null)
                lobbyManager = PhotonLobbyManager.EnsureInstance();

            if (lobbyManager == null)
            {
                SetStatus(hostStatusText, "Photon Lobby Manager is unavailable.");
                return;
            }

            if (lobbyManager.IsBusy || lobbyManager.IsInRoom)
            {
                SetStatus(hostStatusText, lobbyManager.StatusMessage);
                return;
            }

            string requestedCode = hostRoomCodeInput != null
                ? hostRoomCodeInput.text
                : string.Empty;

            SetStatus(hostStatusText, "Creating room...");

            lobbyManager.CreateRoom(requestedCode);
            RefreshConnectionStatus();
        }

        // Join Lobby

        public void JoinRoom()
        {
            if (lobbyManager == null)
                lobbyManager = PhotonLobbyManager.EnsureInstance();

            if (lobbyManager == null)
            {
                SetStatus(joinStatusText, "Photon Lobby Manager is unavailable.");
                return;
            }

            if (lobbyManager.IsBusy || lobbyManager.IsInRoom)
            {
                SetStatus(joinStatusText, lobbyManager.StatusMessage);
                return;
            }

            string requestedCode = joinRoomCodeInput != null
                ? joinRoomCodeInput.text
                : string.Empty;

            SetStatus(joinStatusText, "Joining room...");

            lobbyManager.JoinRoom(requestedCode);
            RefreshConnectionStatus();
        }

        // How To Play

        public void ShowThiefGuide()
        {
            if (thiefGuideContent != null)
                thiefGuideContent.SetActive(true);

            if (ownerGuideContent != null)
                ownerGuideContent.SetActive(false);
        }

        public void ShowOwnerGuide()
        {
            if (thiefGuideContent != null)
                thiefGuideContent.SetActive(false);

            if (ownerGuideContent != null)
                ownerGuideContent.SetActive(true);
        }

        // Back buttons

        public void BackToMainMenu()
        {
            if (lobbyManager != null && lobbyManager.IsBusy)
            {
                RefreshConnectionStatus();
                return;
            }

            if (lobbyManager != null && lobbyManager.IsInRoom)
            {
                const string message =
                    "You are already connected. Leave the room before returning to the main menu.";

                SetStatus(hostStatusText, message);
                SetStatus(joinStatusText, message);
                return;
            }

            ShowMainPanel();
        }

        // Quit confirmation

        public void ConfirmQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // Photon status

        private void RefreshConnectionStatus()
        {
            if (lobbyManager == null)
                return;

            string message = lobbyManager.StatusMessage;

            if (lobbyManager.State == PhotonLobbyState.InRoom)
            {
                message = string.IsNullOrEmpty(lobbyManager.RoomCode)
                    ? "Connected. Waiting for the Lobby..."
                    : $"Connected to room {lobbyManager.RoomCode}.";
            }

            SetStatus(hostStatusText, message);
            SetStatus(joinStatusText, message);

            bool canConnect =
                !lobbyManager.IsBusy &&
                !lobbyManager.IsInRoom;

            if (createRoomButton != null)
                createRoomButton.interactable = canConnect;

            if (joinRoomButton != null)
                joinRoomButton.interactable = canConnect;

            if (hostRoomCodeInput != null)
                hostRoomCodeInput.interactable = canConnect;

            if (joinRoomCodeInput != null)
                joinRoomCodeInput.interactable = canConnect;

            bool canGoBack =
                !lobbyManager.IsBusy &&
                !lobbyManager.IsInRoom;

            if (hostBackToMenuButton != null)
                hostBackToMenuButton.interactable = canGoBack;

            if (joinBackToMenuButton != null)
                joinBackToMenuButton.interactable = canGoBack;

            if (offlineBackToMenuButton != null)
                offlineBackToMenuButton.interactable = true;
        }

        // Panel visibility

        private void ShowOnly(GameObject selected)
        {
            SetActive(mainPanel, selected == null);
            SetActive(hostGamePanel, selected == hostGamePanel);
            SetActive(joinLobbyPanel, selected == joinLobbyPanel);
            SetActive(playOfflinePanel, selected == playOfflinePanel);
            SetActive(howToPlayPanel, selected == howToPlayPanel);
            SetActive(optionsPanel, selected == optionsPanel);
            SetActive(quitConfirmPanel, selected == quitConfirmPanel);
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null)
                target.SetActive(active);
        }

        private static void SetActive(Button target, bool active)
        {
            if (target != null)
                target.gameObject.SetActive(active);
        }

        private static void SetStatus(TMP_Text target, string value)
        {
            if (target != null)
                target.text = value;
        }
    }
}
