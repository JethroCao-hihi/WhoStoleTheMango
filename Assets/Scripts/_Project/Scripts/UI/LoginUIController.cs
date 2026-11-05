using System.Collections;
using MangoHeist.Auth;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MangoHeist.UI
{
    /// <summary>
    /// Connects the LoginScene controls authored in Unity to the authentication flow.
    /// This component does not create, delete, or resize UI objects.
    /// </summary>
    public sealed class LoginUIController : MonoBehaviour
    {
        [Header("Scene-authored panels")]
        [SerializeField] private GameObject mainCard;
        [SerializeField] private GameObject profileCard;
        [SerializeField] private GameObject toastPopup;
        [SerializeField] private GameObject emailRow;
        [SerializeField] private GameObject confirmPasswordRow;

        [Header("Scene-authored inputs")]
        [SerializeField] private TMP_InputField usernameInput;
        [SerializeField] private TMP_InputField emailInput;
        [SerializeField] private TMP_InputField passwordInput;
        [SerializeField] private TMP_InputField confirmPasswordInput;

        [Header("Scene-authored buttons")]
        [SerializeField] private Button submitButton;
        [SerializeField] private Button guestButton;
        [SerializeField] private Button switchModeButton;
        [SerializeField] private Button playButton;
        [SerializeField] private Button logoutButton;

        [Header("Scene-authored labels")]
        [SerializeField] private TMP_Text subtitleText;
        [SerializeField] private TMP_Text submitButtonLabel;
        [SerializeField] private TMP_Text switchModeButtonLabel;
        [SerializeField] private TMP_Text toastText;

        private bool isRegisterMode;
        private bool isLoadingMenu;
        private Coroutine toastCoroutine;

        private void Awake()
        {
            BindSceneAuthoredControls();
            WarnAboutMissingRequiredControls();
        }

        private void OnEnable()
        {
            AddListener(submitButton, OnSubmitClicked);
            AddListener(guestButton, OnGuestClicked);
            AddListener(switchModeButton, ToggleRegisterMode);
            AddListener(playButton, OnPlayClicked);
            AddListener(logoutButton, OnLogoutClicked);
        }

        private void OnDisable()
        {
            RemoveListener(submitButton, OnSubmitClicked);
            RemoveListener(guestButton, OnGuestClicked);
            RemoveListener(switchModeButton, ToggleRegisterMode);
            RemoveListener(playButton, OnPlayClicked);
            RemoveListener(logoutButton, OnLogoutClicked);
        }

        private void Start()
        {
            SetRegisterMode(false);
            if (toastPopup != null)
                toastPopup.SetActive(false);
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.tabKey.wasPressedThisFrame)
                FocusNextInput();

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                OnSubmitClicked();
#endif
        }

        public void SetRegisterMode(bool registerMode)
        {
            isRegisterMode = registerMode;

            // Email registration is not supported by the current AuthManager flow.
            if (emailRow != null)
                emailRow.SetActive(false);
            if (confirmPasswordRow != null)
                confirmPasswordRow.SetActive(registerMode);

            if (submitButtonLabel != null)
                submitButtonLabel.text = registerMode ? "XÁC NHẬN ĐĂNG KÝ" : "ĐĂNG NHẬP";
            if (switchModeButtonLabel != null)
                switchModeButtonLabel.text = registerMode
                    ? "QUAY LẠI ĐĂNG NHẬP"
                    : "ĐĂNG KÝ TÀI KHOẢN MỚI";
            if (subtitleText != null)
                subtitleText.text = registerMode
                    ? "MANGO HEIST - ĐĂNG KÝ TÀI KHOẢN MỚI"
                    : "MANGO HEIST - HỆ THỐNG ĐĂNG NHẬP";
        }

        public void ToggleRegisterMode() => SetRegisterMode(!isRegisterMode);

        public void OnSubmitClicked()
        {
            string username = usernameInput != null ? usernameInput.text.Trim() : string.Empty;
            string password = passwordInput != null ? passwordInput.text : string.Empty;

            if (AuthManager.Instance == null)
            {
                ShowToast(AuthErrorCodes.DB_CONNECT_TIMEOUT);
                return;
            }

            if (isRegisterMode)
            {
                string confirmation = confirmPasswordInput != null
                    ? confirmPasswordInput.text
                    : string.Empty;

                AuthManager.Instance.Register(username, string.Empty, password, confirmation,
                    (success, message) =>
                    {
                        if (success)
                            OnLoginSuccess();
                        else
                            ShowToast(message);
                    });
            }
            else
            {
                AuthManager.Instance.Login(username, password, (success, message) =>
                {
                    if (success)
                        OnLoginSuccess();
                    else
                        ShowToast(message);
                });
            }
        }

        public void OnGuestClicked()
        {
            SceneFlowState.EnterOffline();
            LoadMenuScene();
        }

        public void OnPlayClicked() => LoadMenuScene();

        public void OnLogoutClicked()
        {
            if (AuthManager.Instance != null)
                AuthManager.Instance.Logout();

            if (profileCard != null) profileCard.SetActive(false);
            if (mainCard != null) mainCard.SetActive(true);
            if (passwordInput != null) passwordInput.text = string.Empty;
            if (confirmPasswordInput != null) confirmPasswordInput.text = string.Empty;
            ShowToast("Đã đăng xuất an toàn.", new Color32(59, 130, 246, 255));
        }

        public void ShowToast(string message, Color32? backgroundColor = null)
        {
            if (toastPopup == null || toastText == null)
                return;

            if (toastCoroutine != null)
                StopCoroutine(toastCoroutine);

            toastText.text = message;
            Image background = toastPopup.GetComponent<Image>();
            if (background != null)
                background.color = backgroundColor ?? new Color32(127, 29, 29, 245);

            toastCoroutine = StartCoroutine(ToastRoutine(3f));
        }

        private void BindSceneAuthoredControls()
        {
            mainCard = FindObject(mainCard, "MainCard");
            profileCard = FindObject(profileCard, "ProfileCard");
            toastPopup = FindObject(toastPopup, "ToastPopup");
            emailRow = FindObject(emailRow, "Input_Email");
            confirmPasswordRow = FindObject(confirmPasswordRow, "Input_ConfirmPassword");

            usernameInput = FindComponent(usernameInput, "Input_Username");
            emailInput = FindComponent(emailInput, "Input_Email");
            passwordInput = FindComponent(passwordInput, "Input_Password");
            confirmPasswordInput = FindComponent(confirmPasswordInput, "Input_ConfirmPassword");

            submitButton = FindComponent(submitButton, "Btn_Login");
            guestButton = FindComponent(guestButton, "Btn_Guest");
            switchModeButton = FindComponent(switchModeButton, "Btn_SwitchMode");
            playButton = FindComponent(playButton, "Btn_Lobby");
            logoutButton = FindComponent(logoutButton, "Btn_Logout");

            subtitleText = FindComponent(subtitleText, "Subtitle");
            submitButtonLabel = FindLabel(submitButtonLabel, submitButton);
            switchModeButtonLabel = FindLabel(switchModeButtonLabel, switchModeButton);
            toastText = FindComponent(toastText, "ToastMessage");
        }

        private void WarnAboutMissingRequiredControls()
        {
            if (usernameInput == null || passwordInput == null || submitButton == null ||
                switchModeButton == null || guestButton == null)
            {
                Debug.LogWarning(
                    "LoginUIController is missing required scene-authored controls. Assign them in the Inspector or use the expected GameObject names.",
                    this);
            }
        }

        private void FocusNextInput()
        {
            if (usernameInput != null && usernameInput.isFocused)
            {
                passwordInput?.ActivateInputField();
                return;
            }

            if (passwordInput != null && passwordInput.isFocused)
            {
                if (isRegisterMode && confirmPasswordInput != null)
                    confirmPasswordInput.ActivateInputField();
                else
                    usernameInput?.ActivateInputField();
                return;
            }

            if (confirmPasswordInput != null && confirmPasswordInput.isFocused)
                usernameInput?.ActivateInputField();
        }

        private void OnLoginSuccess()
        {
            SceneFlowState.EnterOnline();
            LoadMenuScene();
        }

        private void LoadMenuScene()
        {
            if (isLoadingMenu)
                return;

            isLoadingMenu = true;
            try
            {
                AsyncOperation operation = SceneManager.LoadSceneAsync("MenuScene");
                if (operation == null)
                    throw new System.InvalidOperationException("MenuScene is not in Build Settings.");
            }
            catch (System.Exception exception)
            {
                isLoadingMenu = false;
                Debug.LogException(exception, this);
                ShowToast("Could not load MenuScene. Check Build Settings.");
            }
        }

        private IEnumerator ToastRoutine(float duration)
        {
            toastPopup.SetActive(true);
            yield return new WaitForSeconds(duration);
            toastPopup.SetActive(false);
            toastCoroutine = null;
        }

        private static GameObject FindObject(GameObject assigned, string objectName)
        {
            if (assigned != null)
                return assigned;

            foreach (Transform child in Object.FindObjectsByType<Transform>(
                         FindObjectsInactive.Include))
            {
                if (child.gameObject.scene == SceneManager.GetActiveScene() && child.name == objectName)
                    return child.gameObject;
            }

            return null;
        }

        private T FindComponent<T>(T assigned, string objectName) where T : Component
        {
            if (assigned != null)
                return assigned;

            foreach (Transform child in Object.FindObjectsByType<Transform>(
                         FindObjectsInactive.Include))
            {
                if (child.gameObject.scene != gameObject.scene || child.name != objectName)
                    continue;

                T component = child.GetComponent<T>();
                if (component != null)
                    return component;
            }

            return null;
        }

        private static TMP_Text FindLabel(TMP_Text assigned, Button button)
        {
            return assigned != null || button == null
                ? assigned
                : button.GetComponentInChildren<TMP_Text>(true);
        }

        private static void AddListener(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
                button.onClick.AddListener(action);
        }

        private static void RemoveListener(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
                button.onClick.RemoveListener(action);
        }
    }
}
