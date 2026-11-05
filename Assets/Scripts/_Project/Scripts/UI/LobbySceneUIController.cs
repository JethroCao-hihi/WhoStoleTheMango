using MangoHeist.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MangoHeist.UI
{
    public sealed class LobbySceneUIController : MonoBehaviour
    {
        [Header("Room Information")]
        [SerializeField] private TMP_Text roomCodeText;       // Hiển thị ID Phòng
        [SerializeField] private TMP_Text playerCountText;    // Hiển thị số lượng người (VD: 1/2 hoặc 1/5)
        [SerializeField] private TMP_Text playerListText;     // Danh sách tên và role người chơi
        [SerializeField] private TMP_Text statusText;         // Trạng thái kết nối

        [Header("Role Selection Panel")]
        [SerializeField] private GameObject roleSelectionPanel; // Panel chứa 2 nút chọn phe
        [SerializeField] private Button openRolePanelButton;    // Nút mở Panel chọn phe (Nút Customize hoặc nút Chọn Phe)
        [SerializeField] private Button chooseThiefButton;      // Nút chọn phe: Trộm Xoài
        [SerializeField] private Button chooseOwnerButton;      // Nút chọn phe: Chủ Vườn
        [SerializeField] private Button closeRolePanelButton;   // Nút đóng Panel chọn phe

        [Header("Lobby Actions")]
        [SerializeField] private Button startGameButton;      // Nút Start (Chỉ Host thấy)
        [SerializeField] private Button leaveRoomButton;      // Nút Rời phòng

        private PhotonLobbyManager lobbyManager;

        private void Awake()
        {
            lobbyManager = PhotonLobbyManager.EnsureInstance();
            if (roleSelectionPanel != null)
                roleSelectionPanel.SetActive(false); // Mặc định ẩn bảng chọn phe lúc đầu
        }

        private void OnEnable()
        {
            if (lobbyManager == null)
                lobbyManager = PhotonLobbyManager.EnsureInstance();

            if (lobbyManager != null)
                lobbyManager.Changed += Refresh;

            // Đăng ký sự kiện nút bấm
            AddListener(openRolePanelButton, () => ToggleRolePanel(true));
            AddListener(closeRolePanelButton, () => ToggleRolePanel(false));

            AddListener(chooseThiefButton, () => {
                lobbyManager.RequestRoleSelection(NetworkPlayerTeam.Thief);
                ToggleRolePanel(false);
            });

            AddListener(chooseOwnerButton, () => {
                lobbyManager.RequestRoleSelection(NetworkPlayerTeam.OrchardOwner);
                ToggleRolePanel(false);
            });

            AddListener(startGameButton, () => lobbyManager.StartGame());
            AddListener(leaveRoomButton, () => _ = lobbyManager.LeaveAndReturnToMenuAsync());

            Refresh();
        }

        private void OnDisable()
        {
            if (lobbyManager != null)
                lobbyManager.Changed -= Refresh;

            RemoveListener(openRolePanelButton, () => ToggleRolePanel(true));
            RemoveListener(closeRolePanelButton, () => ToggleRolePanel(false));
            RemoveListener(chooseThiefButton, null);
            RemoveListener(chooseOwnerButton, null);
            RemoveListener(startGameButton, () => lobbyManager.StartGame());
            RemoveListener(leaveRoomButton, () => _ = lobbyManager.LeaveAndReturnToMenuAsync());
        }

        private void ToggleRolePanel(bool isOpen)
        {
            if (roleSelectionPanel != null)
                roleSelectionPanel.SetActive(isOpen);
        }

        private void Refresh()
        {
            if (lobbyManager == null)
                return;

            // 1. Hiển thị Status
            SetText(statusText, lobbyManager.StatusMessage);

            // 2. Hiển thị ID Phòng để người khác nhìn thấy và nhập vào Join Menu
            SetText(roomCodeText, string.IsNullOrEmpty(lobbyManager.RoomCode)
                ? "Room ID: ---"
                : $"Room ID: {lobbyManager.RoomCode}");

            // 3. Hiển thị số người trong phòng / Tổng số slot tối đa
            int currentPlayers = lobbyManager.PlayerLabels.Count;
            int maxPlayers = PhotonLobbyManager.RoomCapacity;
            SetText(playerCountText, $"Players: {currentPlayers} / {maxPlayers}");

            // 4. Hiển thị danh sách người chơi và role đã nhận
            SetText(playerListText, currentPlayers == 0
                ? "Waiting for players..."
                : string.Join("\n", lobbyManager.PlayerLabels));

            // 5. Kiểm tra quyền chọn phe
            bool canChooseRole = lobbyManager.CanChooseRole && !lobbyManager.IsRoleRequestPending;
            if (openRolePanelButton != null)
                openRolePanelButton.interactable = canChooseRole;

            // 6. Nút Start chỉ dành cho Host khi đủ điều kiện
            if (startGameButton != null)
            {
                startGameButton.gameObject.SetActive(lobbyManager.IsHost);
                startGameButton.interactable = lobbyManager.CanStartGame;
            }

            // 7. Nút Rời phòng
            if (leaveRoomButton != null)
                leaveRoomButton.interactable = !lobbyManager.IsBusy;
        }

        private static void SetText(TMP_Text target, string value)
        {
            if (target != null) target.text = value;
        }

        private static void AddListener(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null) button.onClick.AddListener(action);
        }

        private static void RemoveListener(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null) button.onClick.RemoveListener(action);
        }
    }
}