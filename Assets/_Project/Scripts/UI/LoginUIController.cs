using System.Collections;
using MangoHeist.Auth;
using MangoHeist.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MangoHeist.UI
{
    public class LoginUIController : MonoBehaviour
    {
        [Header("=== KHUNG CHÍNH (PANELS) ===")]
        [SerializeField] private GameObject _mainCardPanel;
        [SerializeField] private GameObject _loginContainer;
        [SerializeField] private GameObject _registerContainer;
        [SerializeField] private GameObject _profilePanel;

        [Header("=== FORM ĐĂNG NHẬP ===")]
        [SerializeField] private TMP_InputField _loginUsernameInput;
        [SerializeField] private TMP_InputField _loginPasswordInput;
        [SerializeField] private Button _loginSubmitButton;
        [SerializeField] private Button _switchToRegisterButton;
        [SerializeField] private Button _guestButton;
        [SerializeField] private Button _toggleLoginPasswordButton;
        [SerializeField] private TextMeshProUGUI _toggleLoginEyeIcon;

        [Header("=== FORM ĐĂNG KÝ ===")]
        [SerializeField] private TMP_InputField _regUsernameInput;
        [SerializeField] private TMP_InputField _regEmailInput;
        [SerializeField] private TMP_InputField _regPasswordInput;
        [SerializeField] private TMP_InputField _regConfirmPasswordInput;
        [SerializeField] private Button _regSubmitButton;
        [SerializeField] private Button _switchToLoginButton;
        [SerializeField] private Button _toggleRegPasswordButton;
        [SerializeField] private TextMeshProUGUI _toggleRegEyeIcon;

        [Header("=== THÔNG BÁO LỖI / TOAST POPUP ===")]
        [SerializeField] private GameObject _toastPopup;
        [SerializeField] private TextMeshProUGUI _toastMessageText;

        [Header("=== HỒ SƠ SAU KHI ĐĂNG NHẬP ===")]
        [SerializeField] private TextMeshProUGUI _welcomeText;
        [SerializeField] private TextMeshProUGUI _levelText;
        [SerializeField] private TextMeshProUGUI _coinsText;
        [SerializeField] private TextMeshProUGUI _unlockedMapsText;
        [SerializeField] private Button _playOnlineButton;
        [SerializeField] private Button _logoutButton;

        private Coroutine _toastCoroutine;
        private bool _isLoginPasswordVisible = false;
        private bool _isRegPasswordVisible = false;

        private void Awake()
        {
            var standalone = FindAnyObjectByType<UnityEngine.EventSystems.StandaloneInputModule>();
            if (standalone != null)
            {
                var esGo = standalone.gameObject;
                if (Application.isPlaying) Destroy(standalone);
                else DestroyImmediate(standalone);

                if (esGo.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() == null)
                {
                    esGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                }
            }
        }

        private void Start()
        {
            // Sự kiện Form Đăng nhập
            if (_loginSubmitButton != null) _loginSubmitButton.onClick.AddListener(OnLoginSubmit);
            if (_switchToRegisterButton != null) _switchToRegisterButton.onClick.AddListener(SwitchToRegisterMode);
            if (_guestButton != null) _guestButton.onClick.AddListener(OnGuestClick);
            if (_toggleLoginPasswordButton != null) _toggleLoginPasswordButton.onClick.AddListener(ToggleLoginPasswordVisibility);

            // Sự kiện Form Đăng ký
            if (_regSubmitButton != null) _regSubmitButton.onClick.AddListener(OnRegisterSubmit);
            if (_switchToLoginButton != null) _switchToLoginButton.onClick.AddListener(SwitchToLoginMode);
            if (_toggleRegPasswordButton != null) _toggleRegPasswordButton.onClick.AddListener(ToggleRegPasswordVisibility);

            // Hồ sơ
            if (_logoutButton != null) _logoutButton.onClick.AddListener(OnLogoutClick);
            if (_playOnlineButton != null) _playOnlineButton.onClick.AddListener(() => ShowToast("Đang kết nối phòng đấu...", Color.cyan));

            // Đăng ký sự kiện từ AuthManager
            AuthManager.Instance.OnLoginSuccess += OnAuthSuccess;
            AuthManager.Instance.OnLoggedOut += OnAuthLogout;

            SwitchToLoginMode();
            if (_toastPopup != null) _toastPopup.SetActive(false);
        }

        private void OnDestroy()
        {
            if (AuthManager.Instance != null)
            {
                AuthManager.Instance.OnLoginSuccess -= OnAuthSuccess;
                AuthManager.Instance.OnLoggedOut -= OnAuthLogout;
            }
        }

        private void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return;

            // Chuyển tab phím Tab
            if (keyboard.tabKey.wasPressedThisFrame)
            {
                if (_loginContainer.activeSelf)
                {
                    if (_loginUsernameInput.isFocused) _loginPasswordInput.ActivateInputField();
                    else if (_loginPasswordInput.isFocused) _loginUsernameInput.ActivateInputField();
                }
                else if (_registerContainer.activeSelf)
                {
                    if (_regUsernameInput.isFocused) _regEmailInput.ActivateInputField();
                    else if (_regEmailInput.isFocused) _regPasswordInput.ActivateInputField();
                    else if (_regPasswordInput.isFocused) _regConfirmPasswordInput.ActivateInputField();
                    else if (_regConfirmPasswordInput.isFocused) _regUsernameInput.ActivateInputField();
                }
            }

            // Nhấn Enter để submit
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                if (_loginContainer.activeSelf) OnLoginSubmit();
                else if (_registerContainer.activeSelf) OnRegisterSubmit();
            }
        }

        public void SwitchToLoginMode()
        {
            if (_mainCardPanel != null) _mainCardPanel.SetActive(true);
            if (_loginContainer != null) _loginContainer.SetActive(true);
            if (_registerContainer != null) _registerContainer.SetActive(false);
            if (_profilePanel != null) _profilePanel.SetActive(false);
        }

        public void SwitchToRegisterMode()
        {
            if (_mainCardPanel != null) _mainCardPanel.SetActive(true);
            if (_loginContainer != null) _loginContainer.SetActive(false);
            if (_registerContainer != null) _registerContainer.SetActive(true);
            if (_profilePanel != null) _profilePanel.SetActive(false);
        }

        private void OnLoginSubmit()
        {
            string u = _loginUsernameInput != null ? _loginUsernameInput.text : "";
            string p = _loginPasswordInput != null ? _loginPasswordInput.text : "";

            ShowToast("Đang kiểm tra...", Color.yellow);
            AuthManager.Instance.Login(u, p, (success, msg) =>
            {
                ShowToast(msg, success ? new Color(0.2f, 0.9f, 0.4f) : new Color(1f, 0.35f, 0.35f));
            });
        }

        private void OnRegisterSubmit()
        {
            string u = _regUsernameInput != null ? _regUsernameInput.text : "";
            string e = _regEmailInput != null ? _regEmailInput.text : "";
            string p = _regPasswordInput != null ? _regPasswordInput.text : "";
            string cp = _regConfirmPasswordInput != null ? _regConfirmPasswordInput.text : "";

            ShowToast("Đang tạo tài khoản...", Color.yellow);
            AuthManager.Instance.Register(u, e, p, cp, (success, msg) =>
            {
                ShowToast(msg, success ? new Color(0.2f, 0.9f, 0.4f) : new Color(1f, 0.35f, 0.35f));
            });
        }

        private void OnGuestClick()
        {
            ShowToast("Đang vào bằng tài khoản Khách...", Color.yellow);
            AuthManager.Instance.PlayAsGuest((success, msg) =>
            {
                ShowToast(msg, new Color(0.2f, 0.9f, 0.4f));
            });
        }

        private void OnLogoutClick()
        {
            AuthManager.Instance.Logout();
            if (_loginPasswordInput != null) _loginPasswordInput.text = "";
            if (_regPasswordInput != null) _regPasswordInput.text = "";
            if (_regConfirmPasswordInput != null) _regConfirmPasswordInput.text = "";
            ShowToast("Đã đăng xuất.", Color.white);
        }

        private void ToggleLoginPasswordVisibility()
        {
            _isLoginPasswordVisible = !_isLoginPasswordVisible;
            if (_loginPasswordInput != null)
            {
                _loginPasswordInput.contentType = _isLoginPasswordVisible ? TMP_InputField.ContentType.Standard : TMP_InputField.ContentType.Password;
                _loginPasswordInput.ForceLabelUpdate();
            }
            if (_toggleLoginEyeIcon != null)
            {
                _toggleLoginEyeIcon.text = _isLoginPasswordVisible ? "ẨN" : "HIỆN";
            }
        }

        private void ToggleRegPasswordVisibility()
        {
            _isRegPasswordVisible = !_isRegPasswordVisible;
            if (_regPasswordInput != null)
            {
                _regPasswordInput.contentType = _isRegPasswordVisible ? TMP_InputField.ContentType.Standard : TMP_InputField.ContentType.Password;
                _regPasswordInput.ForceLabelUpdate();
            }
            if (_regConfirmPasswordInput != null)
            {
                _regConfirmPasswordInput.contentType = _isRegPasswordVisible ? TMP_InputField.ContentType.Standard : TMP_InputField.ContentType.Password;
                _regConfirmPasswordInput.ForceLabelUpdate();
            }
            if (_toggleRegEyeIcon != null)
            {
                _toggleRegEyeIcon.text = _isRegPasswordVisible ? "ẨN" : "HIỆN";
            }
        }

        private void OnAuthSuccess(UserAccount account)
        {
            if (_mainCardPanel != null) _mainCardPanel.SetActive(false);
            if (_profilePanel != null) _profilePanel.SetActive(true);

            var saveData = SaveManager.Instance != null ? SaveManager.Instance.CurrentData : null;
            if (_welcomeText != null) _welcomeText.text = $"Xin chào, <color=#FFD54F>{account.username}</color>!";
            if (saveData != null)
            {
                if (_levelText != null) _levelText.text = $"Cấp độ: {saveData.playerLevel} (EXP: {saveData.playerExp})";
                if (_coinsText != null) _coinsText.text = $"Xu Xoài: <color=#FFA000>{saveData.mangoCoins} Xu</color>";
                if (_unlockedMapsText != null) _unlockedMapsText.text = $"Map đã mở: {saveData.unlockedMapIds.Count} map";
            }
        }

        private void OnAuthLogout()
        {
            SwitchToLoginMode();
        }

        public void ShowToast(string message, Color color)
        {
            if (_toastPopup == null || _toastMessageText == null) return;
            _toastMessageText.text = message;
            _toastMessageText.color = color;

            if (_toastCoroutine != null) StopCoroutine(_toastCoroutine);
            _toastCoroutine = StartCoroutine(ToastRoutine());
        }

        private IEnumerator ToastRoutine()
        {
            _toastPopup.SetActive(true);
            yield return new WaitForSeconds(3.5f);
            _toastPopup.SetActive(false);
        }
    }
}
