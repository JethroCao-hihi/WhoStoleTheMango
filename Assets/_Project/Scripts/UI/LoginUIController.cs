using System.Collections;
using MangoHeist.Auth;
using MangoHeist.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MangoHeist.UI
{
    /// <summary>
    /// Điều khiển toàn bộ giao diện Màn hình Đăng nhập (Login Scene):
    /// - Nhận input tên tài khoản / mật khẩu
    /// - Xử lý sự kiện bấm nút Đăng nhập / Đăng ký / Chơi Khách
    /// - Hiển thị thông báo trạng thái màu sắc
    /// - Hiển thị bảng Profile người chơi (Level, Xu, Map đã mở) sau khi đăng nhập thành công.
    /// </summary>
    public class LoginUIController : MonoBehaviour
    {
        [Header("=== KHUNG ĐĂNG NHẬP (LOGIN PANEL) ===")]
        [SerializeField] private GameObject _loginPanel;
        [SerializeField] private TMP_InputField _usernameInput;
        [SerializeField] private TMP_InputField _passwordInput;
        [SerializeField] private Button _loginButton;
        [SerializeField] private Button _registerButton;
        [SerializeField] private Button _guestButton;
        [SerializeField] private TextMeshProUGUI _statusText;

        [Header("=== KHUNG HỒ SƠ NGƯỜI CHƠI (PROFILE PANEL) ===")]
        [SerializeField] private GameObject _profilePanel;
        [SerializeField] private TextMeshProUGUI _welcomeText;
        [SerializeField] private TextMeshProUGUI _levelText;
        [SerializeField] private TextMeshProUGUI _coinsText;
        [SerializeField] private TextMeshProUGUI _unlockedMapsText;
        [SerializeField] private Button _playOnlineButton;
        [SerializeField] private Button _logoutButton;

        private void Awake()
        {
            // Tự động sửa lỗi xung đột giữa New Input System và StandaloneInputModule
            var standalone = FindAnyObjectByType<UnityEngine.EventSystems.StandaloneInputModule>();
            if (standalone != null)
            {
                var esGo = standalone.gameObject;
                if (Application.isPlaying)
                {
                    Destroy(standalone);
                }
                else
                {
                    DestroyImmediate(standalone);
                }

                if (esGo.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() == null)
                {
                    esGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                }
            }
        }

        private void Start()
        {
            // Gán sự kiện cho các nút bấm
            if (_loginButton != null) _loginButton.onClick.AddListener(OnLoginClicked);
            if (_registerButton != null) _registerButton.onClick.AddListener(OnRegisterClicked);
            if (_guestButton != null) _guestButton.onClick.AddListener(OnGuestClicked);
            if (_logoutButton != null) _logoutButton.onClick.AddListener(OnLogoutClicked);
            if (_playOnlineButton != null) _playOnlineButton.onClick.AddListener(OnPlayOnlineClicked);

            // Đăng ký sự kiện từ AuthManager
            AuthManager.Instance.OnLoginSuccess += ShowProfilePanel;
            AuthManager.Instance.OnLoggedOut += ShowLoginPanel;

            // Giới hạn ký tự input để không làm vỡ giao diện (Fix Bug 4)
            if (_usernameInput != null) _usernameInput.characterLimit = 16;
            if (_passwordInput != null) _passwordInput.characterLimit = 20;

            ShowLoginPanel();
            SetStatus("", Color.white);
        }

        private void Update()
        {
            // Hỗ trợ phím Tab và Enter chuẩn New Input System cho PC (Fix Bug 5)
            if (_loginPanel != null && _loginPanel.activeSelf)
            {
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                if (keyboard == null) return;

                // Nhấn Tab để chuyển đổi giữa Username và Password
                if (keyboard.tabKey.wasPressedThisFrame)
                {
                    if (_usernameInput != null && _usernameInput.isFocused && _passwordInput != null)
                    {
                        _passwordInput.ActivateInputField();
                    }
                    else if (_passwordInput != null && _passwordInput.isFocused && _usernameInput != null)
                    {
                        _usernameInput.ActivateInputField();
                    }
                }

                // Nhấn Enter để Đăng Nhập
                if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                {
                    OnLoginClicked();
                }
            }
        }

        private void OnDestroy()
        {
            if (AuthManager.Instance != null)
            {
                AuthManager.Instance.OnLoginSuccess -= ShowProfilePanel;
                AuthManager.Instance.OnLoggedOut -= ShowLoginPanel;
            }
        }

        private void OnLoginClicked()
        {
            string u = _usernameInput != null ? _usernameInput.text : "";
            string p = _passwordInput != null ? _passwordInput.text : "";

            SetStatus("Đang kiểm tra tài khoản...", Color.yellow);
            AuthManager.Instance.Login(u, p, (success, msg) =>
            {
                SetStatus(msg, success ? Color.green : Color.red);
            });
        }

        private void OnRegisterClicked()
        {
            string u = _usernameInput != null ? _usernameInput.text : "";
            string p = _passwordInput != null ? _passwordInput.text : "";

            SetStatus("Đang tạo tài khoản mới...", Color.yellow);
            AuthManager.Instance.Register(u, p, (success, msg) =>
            {
                SetStatus(msg, success ? Color.green : Color.red);
            });
        }

        private void OnGuestClicked()
        {
            SetStatus("Đang đăng nhập chế độ Khách...", Color.yellow);
            AuthManager.Instance.PlayAsGuest((success, msg) =>
            {
                SetStatus(msg, Color.green);
            });
        }

        private void OnLogoutClicked()
        {
            AuthManager.Instance.Logout();
            // Xóa sạch mật khẩu khỏi ô nhập khi đăng xuất để bảo mật (Fix Bug 3)
            if (_passwordInput != null) _passwordInput.text = "";
            SetStatus("Đã đăng xuất an toàn.", Color.white);
        }

        private void OnPlayOnlineClicked()
        {
            Debug.Log("<color=#4CAF50>▶ Chuyển sang Sảnh Chờ (Lobby Online) hoặc Chọn Map!</color>");
            SetStatus("Đang kết nối tới phòng chờ...", Color.cyan);
            // Sau này khi Peter ghép sảnh Fusion vào, chỉ cần gọi:
            // UnityEngine.SceneManagement.SceneManager.LoadScene("LobbyScene");
        }

        private void ShowLoginPanel()
        {
            if (_loginPanel != null) _loginPanel.SetActive(true);
            if (_profilePanel != null) _profilePanel.SetActive(false);
        }

        private void ShowProfilePanel(UserAccount account)
        {
            if (_loginPanel != null) _loginPanel.SetActive(false);
            if (_profilePanel != null) _profilePanel.SetActive(true);

            var saveData = SaveManager.Instance != null ? SaveManager.Instance.CurrentData : null;

            if (_welcomeText != null)
                _welcomeText.text = $"Xin chào, <color=#FFD54F>{account.username}</color>!";

            if (saveData != null)
            {
                if (_levelText != null) _levelText.text = $"Cấp độ: {saveData.playerLevel} (EXP: {saveData.playerExp})";
                if (_coinsText != null) _coinsText.text = $"Xu Xoài: <color=#FFA000>{saveData.mangoCoins} Xu</color>";
                if (_unlockedMapsText != null)
                    _unlockedMapsText.text = $"Map đã mở: {saveData.unlockedMapIds.Count} map ({string.Join(", ", saveData.unlockedMapIds)})";
            }
        }

        private void SetStatus(string message, Color color)
        {
            if (_statusText != null)
            {
                _statusText.text = message;
                _statusText.color = color;
            }
        }
    }
}
