using System.Collections;
using MangoHeist.Auth;
using MangoHeist.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MangoHeist.UI
{
    /// <summary>
    /// Procedural UI Controller toàn diện cho màn hình Đăng Nhập & Đăng Ký.
    /// Tự động dựng 100% uGUI bằng code chuẩn, xóa bỏ mọi rác UI cũ,
    /// chống co giãn lỗi chữ dọc, hỗ trợ bàn phím New Input System, Toast thông báo lỗi.
    /// </summary>
    public class LoginUIController : MonoBehaviour
    {
        private bool _isRegisterMode = false;
        private Coroutine _toastCoroutine;

        // UI GameObjects
        private GameObject _backgroundGo;
        private GameObject _mainCardGo;
        private GameObject _profileCardGo;
        private GameObject _toastPopupGo;

        // Text & Input References
        private TextMeshProUGUI _subtitleText;
        private TMP_InputField _inputUsername;
        private TMP_InputField _inputEmail;
        private GameObject _rowEmail;
        private TMP_InputField _inputPassword;
        private TMP_InputField _inputConfirmPassword;
        private GameObject _rowConfirmPassword;

        // Button Texts & Labels
        private TextMeshProUGUI _lblLoginBtn;
        private TextMeshProUGUI _lblSwitchModeBtn;
        private TextMeshProUGUI _toastText;

        // Profile Card Texts
        private TextMeshProUGUI _txtWelcome;
        private TextMeshProUGUI _txtLevel;
        private TextMeshProUGUI _txtCoins;
        private TextMeshProUGUI _txtMaps;

        private bool _isLoadingGame;

        private void Awake()
        {
            // Đảm bảo Canvas có các thành phần chuẩn
            var canvas = GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            if (GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            RebuildProceduralUI();
        }

        private void Start()
        {
            SetRegisterMode(false);
            if (_toastPopupGo != null) _toastPopupGo.SetActive(false);
            if (_profileCardGo != null) _profileCardGo.SetActive(false);

            if (_inputUsername != null)
            {
                _inputUsername.ActivateInputField();
            }
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // Chuyển Focus bằng phím Tab
            if (keyboard.tabKey.wasPressedThisFrame)
            {
                if (_isRegisterMode)
                {
                    if (_inputUsername != null && _inputUsername.isFocused) _inputPassword.ActivateInputField();
                    else if (_inputPassword != null && _inputPassword.isFocused) _inputConfirmPassword.ActivateInputField();
                    else if (_inputConfirmPassword != null && _inputConfirmPassword.isFocused) _inputUsername.ActivateInputField();
                }
                else
                {
                    if (_inputUsername != null && _inputUsername.isFocused) _inputPassword.ActivateInputField();
                    else if (_inputPassword != null && _inputPassword.isFocused) _inputUsername.ActivateInputField();
                }
            }

            // Enter để Submit
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                OnSubmitClicked();
            }
#endif
        }

        [ContextMenu("Dựng lại toàn bộ UI (Procedural Rebuild)")]
        public void RebuildProceduralUI()
        {
            // Đảm bảo Canvas & CanvasScaler được cấu hình chuẩn với Layer UI
            int uiLayer = LayerMask.NameToLayer("UI");
            gameObject.layer = uiLayer;

            var canvas = GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            if (GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            // ══════════════════════════════════════════════════════════════════
            // QUY TẮC 1: XÓA SẠCH MỌI GAMEOBJECT / UI CŨ TRÊN CANVAS
            // ══════════════════════════════════════════════════════════════════
            while (transform.childCount > 0)
            {
                var child = transform.GetChild(0).gameObject;
                child.SetActive(false);
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    DestroyImmediate(child);
                }
                else
                {
                    child.transform.SetParent(null);
                    Destroy(child);
                }
#else
                child.transform.SetParent(null);
                Destroy(child);
#endif
            }

            // 1. NỀN MÀU #111827 FULL MÀN HÌNH (KHÔNG DÙNG SPRITE ẢNH VẼ SẴN)
            _backgroundGo = new GameObject("Background", typeof(RectTransform));
            _backgroundGo.layer = uiLayer;
            _backgroundGo.transform.SetParent(transform, false);
            var bgRt = _backgroundGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            var bgImg = _backgroundGo.AddComponent<Image>();
            bgImg.sprite = null;
            bgImg.color = new Color32(17, 24, 39, 255); // #111827

            // ══════════════════════════════════════════════════════════════════
            // QUY TẮC 2: KHUNG CHÍNH MAINCARD (460 x 560, KHÔNG BỊ CO WIDTH = 0)
            // ══════════════════════════════════════════════════════════════════
            _mainCardGo = new GameObject("MainCard", typeof(RectTransform));
            _mainCardGo.layer = uiLayer;
            _mainCardGo.transform.SetParent(transform, false);
            var cardRt = _mainCardGo.GetComponent<RectTransform>();
            cardRt.anchorMin = new Vector2(0.5f, 0.5f);
            cardRt.anchorMax = new Vector2(0.5f, 0.5f);
            cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.sizeDelta = new Vector2(460, 560);
            cardRt.anchoredPosition = Vector2.zero;

            var cardImg = _mainCardGo.AddComponent<Image>();
            cardImg.sprite = null;
            cardImg.color = new Color32(31, 41, 55, 255); // #1F2937

            var cardOutline = _mainCardGo.AddComponent<Outline>();
            cardOutline.effectColor = new Color32(55, 65, 81, 255); // #374151
            cardOutline.effectDistance = new Vector2(2, -2);

            var cardShadow = _mainCardGo.AddComponent<Shadow>();
            cardShadow.effectColor = new Color32(0, 0, 0, 120);
            cardShadow.effectDistance = new Vector2(0, -8);

            // Cấu hình VerticalLayoutGroup cực chuẩn
            var vlg = _mainCardGo.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(35, 35, 35, 35);
            vlg.spacing = 16;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;

            // ══════════════════════════════════════════════════════════════════
            // QUY TẮC 3: CÁC COMPONENT THẬT TRÊN GIAO DIỆN
            // ══════════════════════════════════════════════════════════════════

            // 1. Header (Title & Subtitle)
            var titleTxt = CreateTextElement("Title", _mainCardGo.transform, "TRỘM XOÀI ĐẠI CHIẾN", 28, FontStyles.Bold, new Color32(245, 158, 11, 255), 45);
            _subtitleText = CreateTextElement("Subtitle", _mainCardGo.transform, "MANGO HEIST - HỆ THỐNG ĐĂNG NHẬP", 13, FontStyles.Normal, new Color32(156, 163, 175, 255), 25);

            // 2. Input Fields
            _inputUsername = CreateInputField("Input_Username", _mainCardGo.transform, "Nhập tên tài khoản (6-24 ký tự)...", false, out _);
            _inputEmail = CreateInputField("Input_Email", _mainCardGo.transform, "Nhập email (tùy chọn)...", false, out _rowEmail);
            _inputPassword = CreateInputField("Input_Password", _mainCardGo.transform, "Nhập mật khẩu (từ 8 ký tự)...", true, out _);
            _inputConfirmPassword = CreateInputField("Input_ConfirmPassword", _mainCardGo.transform, "Nhập lại mật khẩu...", true, out _rowConfirmPassword);

            // 3. Buttons
            var btnLogin = CreateButton("Btn_Login", _mainCardGo.transform, "ĐĂNG NHẬP", new Color32(16, 185, 129, 255), 52, out _lblLoginBtn);
            btnLogin.onClick.AddListener(OnSubmitClicked);

            var btnSwitch = CreateButton("Btn_SwitchMode", _mainCardGo.transform, "ĐĂNG KÝ TÀI KHOẢN MỚI", new Color32(59, 130, 246, 255), 48, out _lblSwitchModeBtn);
            btnSwitch.onClick.AddListener(() => SetRegisterMode(!_isRegisterMode));

            var btnGuest = CreateButton("Btn_Guest", _mainCardGo.transform, "CHƠI KHÁCH / OFFLINE (BỎ QUA)", new Color32(75, 85, 99, 255), 44, out _);
            btnGuest.onClick.AddListener(OnGuestClicked);

            // ══════════════════════════════════════════════════════════════════
            // QUY TẮC 4: HỘP THÔNG BÁO LỖI THẬT (TOAST NOTIFICATION)
            // ══════════════════════════════════════════════════════════════════
            _toastPopupGo = new GameObject("ToastPopup", typeof(RectTransform));
            _toastPopupGo.layer = uiLayer;
            _toastPopupGo.transform.SetParent(transform, false);
            var toastRt = _toastPopupGo.GetComponent<RectTransform>();
            toastRt.anchorMin = new Vector2(1, 0);
            toastRt.anchorMax = new Vector2(1, 0);
            toastRt.pivot = new Vector2(1, 0);
            toastRt.sizeDelta = new Vector2(380, 80);
            toastRt.anchoredPosition = new Vector2(-25, 25);

            var toastImg = _toastPopupGo.AddComponent<Image>();
            toastImg.sprite = null;
            toastImg.color = new Color32(127, 29, 29, 245); // #7F1D1D đỏ sẫm

            var toastOutline = _toastPopupGo.AddComponent<Outline>();
            toastOutline.effectColor = new Color32(239, 68, 68, 255); // #EF4444 viền đỏ tươi
            toastOutline.effectDistance = new Vector2(2, -2);

            var toastShadow = _toastPopupGo.AddComponent<Shadow>();
            toastShadow.effectColor = new Color32(0, 0, 0, 160);
            toastShadow.effectDistance = new Vector2(0, -4);

            var toastTxtGo = new GameObject("ToastMessage", typeof(RectTransform));
            toastTxtGo.layer = uiLayer;
            toastTxtGo.transform.SetParent(_toastPopupGo.transform, false);

            var toastTxtRt = toastTxtGo.GetComponent<RectTransform>();
            toastTxtRt.anchorMin = Vector2.zero;
            toastTxtRt.anchorMax = Vector2.one;
            toastTxtRt.offsetMin = new Vector2(20, 10);
            toastTxtRt.offsetMax = new Vector2(-20, -10);

            _toastText = toastTxtGo.AddComponent<TextMeshProUGUI>();
            _toastText.text = "";
            _toastText.fontSize = 15f;
            _toastText.fontStyle = FontStyles.Bold;
            _toastText.color = Color.white;
            _toastText.alignment = TextAlignmentOptions.MidlineLeft;
            _toastText.enableWordWrapping = true;
            _toastText.overflowMode = TextOverflowModes.Ellipsis;

            _toastPopupGo.SetActive(false);

            // ══════════════════════════════════════════════════════════════════
            // 5. PROFILE CARD (Hiện sau khi Đăng Nhập / Chơi khách thành công)
            // ══════════════════════════════════════════════════════════════════
            BuildProfileCard();

            SetRegisterMode(false);
        }

        private void BuildProfileCard()
        {
            int uiLayer = LayerMask.NameToLayer("UI");
            _profileCardGo = new GameObject("ProfileCard", typeof(RectTransform));
            _profileCardGo.layer = uiLayer;
            _profileCardGo.transform.SetParent(transform, false);
            var pRt = _profileCardGo.GetComponent<RectTransform>();
            pRt.anchorMin = new Vector2(0.5f, 0.5f);
            pRt.anchorMax = new Vector2(0.5f, 0.5f);
            pRt.pivot = new Vector2(0.5f, 0.5f);
            pRt.sizeDelta = new Vector2(460, 480);
            pRt.anchoredPosition = Vector2.zero;

            var pImg = _profileCardGo.AddComponent<Image>();
            pImg.color = new Color32(31, 41, 55, 255);

            var pOut = _profileCardGo.AddComponent<Outline>();
            pOut.effectColor = new Color32(16, 185, 129, 255);
            pOut.effectDistance = new Vector2(2, -2);

            var vlg = _profileCardGo.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(35, 35, 35, 35);
            vlg.spacing = 16;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;

            CreateTextElement("P_Title", _profileCardGo.transform, "MANGO HEIST - HỒ SƠ", 24, FontStyles.Bold, new Color32(245, 158, 11, 255), 40);
            _txtWelcome = CreateTextElement("P_Welcome", _profileCardGo.transform, "Xin chào người chơi!", 18, FontStyles.Bold, Color.white, 30);
            _txtLevel = CreateTextElement("P_Level", _profileCardGo.transform, "Cấp độ: 1", 16, FontStyles.Normal, new Color32(156, 163, 175, 255), 26);
            _txtCoins = CreateTextElement("P_Coins", _profileCardGo.transform, "Xu Xoài: 100 Xu", 18, FontStyles.Bold, new Color32(245, 158, 11, 255), 30);
            _txtMaps = CreateTextElement("P_Maps", _profileCardGo.transform, "Map đã mở: 1 map", 16, FontStyles.Normal, new Color32(16, 185, 129, 255), 26);

            var btnPlay = CreateButton(
             "Btn_Play",
          _profileCardGo.transform,
         "CHƠI NGAY",
    new Color32(16, 185, 129, 255),
    50,
    out _
);

            btnPlay.onClick.AddListener(OnPlayClicked);

            var btnLogout = CreateButton("Btn_Logout", _profileCardGo.transform, "ĐĂNG XUẤT", new Color32(239, 68, 68, 255), 44, out _);
            btnLogout.onClick.AddListener(OnLogoutClicked);

            _profileCardGo.SetActive(false);
        }

        private TextMeshProUGUI CreateTextElement(string name, Transform parent, string text, float fontSize, FontStyles style, Color color, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleWidth = 1f;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            return tmp;
        }

        private TMP_InputField CreateInputField(string name, Transform parent, string placeholderText, bool isPassword, out GameObject rowGo)
        {
            int uiLayer = LayerMask.NameToLayer("UI");
            rowGo = new GameObject(name, typeof(RectTransform));
            rowGo.layer = uiLayer;
            rowGo.transform.SetParent(parent, false);

            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = 50f;
            le.flexibleWidth = 1f;

            var rowImg = rowGo.AddComponent<Image>();
            rowImg.sprite = null;
            rowImg.color = new Color32(55, 65, 81, 255); // #374151

            var outline = rowGo.AddComponent<Outline>();
            outline.effectColor = new Color32(75, 85, 99, 255); // #4B5563
            outline.effectDistance = new Vector2(1, -1);

            // Text Area Container với RectMask2D (cách lề trái 16px, trên dưới 6px)
            var textAreaGo = new GameObject("Text Area", typeof(RectTransform));
            textAreaGo.layer = uiLayer;
            textAreaGo.transform.SetParent(rowGo.transform, false);
            var areaRt = textAreaGo.GetComponent<RectTransform>();
            areaRt.anchorMin = Vector2.zero;
            areaRt.anchorMax = Vector2.one;
            areaRt.offsetMin = new Vector2(16, 6);
            areaRt.offsetMax = new Vector2(-16, -6);
            textAreaGo.AddComponent<RectMask2D>();

            // Text Component
            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.layer = uiLayer;
            textGo.transform.SetParent(textAreaGo.transform, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            var txt = textGo.AddComponent<TextMeshProUGUI>();
            txt.fontSize = 16f;
            txt.color = Color.white;
            txt.alignment = TextAlignmentOptions.MidlineLeft;
            txt.enableWordWrapping = false;
            txt.overflowMode = TextOverflowModes.Overflow;

            // Placeholder Component
            var phGo = new GameObject("Placeholder", typeof(RectTransform));
            phGo.layer = uiLayer;
            phGo.transform.SetParent(textAreaGo.transform, false);
            var phRt = phGo.GetComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = Vector2.zero;
            phRt.offsetMax = Vector2.zero;

            var ph = phGo.AddComponent<TextMeshProUGUI>();
            ph.text = placeholderText;
            ph.fontSize = 16f;
            ph.fontStyle = FontStyles.Italic;
            ph.color = new Color32(156, 163, 175, 180); // #9CA3AF
            ph.alignment = TextAlignmentOptions.MidlineLeft;
            ph.enableWordWrapping = false;
            ph.overflowMode = TextOverflowModes.Overflow;

            var inputField = rowGo.AddComponent<TMP_InputField>();
            inputField.textViewport = areaRt;
            inputField.textComponent = txt;
            inputField.placeholder = ph;
            
            if (name.Contains("Username"))
            {
                inputField.characterLimit = 24;
            }
            else if (name.Contains("Email"))
            {
                inputField.characterLimit = 64;
            }
            else
            {
                inputField.characterLimit = 32;
            }

            if (isPassword)
            {
                inputField.contentType = TMP_InputField.ContentType.Password;
                inputField.asteriskChar = '*';
            }

            return inputField;
        }

        private Button CreateButton(string name, Transform parent, string label, Color bgColor, float height, out TextMeshProUGUI labelTmp)
        {
            var btnGo = new GameObject(name, typeof(RectTransform));
            btnGo.layer = LayerMask.NameToLayer("UI");
            btnGo.transform.SetParent(parent, false);

            var le = btnGo.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleWidth = 1f;

            var img = btnGo.AddComponent<Image>();
            img.sprite = null;
            img.color = bgColor;

            var btn = btnGo.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = bgColor * 1.15f;
            colors.pressedColor = bgColor * 0.85f;
            btn.colors = colors;

            var txtGo = new GameObject("Text", typeof(RectTransform));
            txtGo.layer = LayerMask.NameToLayer("UI");
            txtGo.transform.SetParent(btnGo.transform, false);
            var txtRt = txtGo.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero;
            txtRt.offsetMax = Vector2.zero;

            labelTmp = txtGo.AddComponent<TextMeshProUGUI>();
            labelTmp.text = label;
            labelTmp.fontSize = 16f;
            labelTmp.fontStyle = FontStyles.Bold;
            labelTmp.color = Color.white;
            labelTmp.alignment = TextAlignmentOptions.Center;
            labelTmp.enableWordWrapping = false;
            labelTmp.overflowMode = TextOverflowModes.Overflow;

            return btn;
        }

        public void SetRegisterMode(bool isRegister)
        {
            _isRegisterMode = isRegister;
            if (_rowEmail != null)
            {
                _rowEmail.SetActive(false);
            }
            if (_rowConfirmPassword != null)
            {
                _rowConfirmPassword.SetActive(isRegister);
            }

            if (_mainCardGo != null)
            {
                var rt = _mainCardGo.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.sizeDelta = new Vector2(460, isRegister ? 640 : 540);
                }
            }

            if (_lblLoginBtn != null)
            {
                _lblLoginBtn.text = isRegister ? "XÁC NHẬN ĐĂNG KÝ" : "ĐĂNG NHẬP";
            }

            if (_lblSwitchModeBtn != null)
            {
                _lblSwitchModeBtn.text = isRegister ? "QUAY LẠI ĐĂNG NHẬP" : "ĐĂNG KÝ TÀI KHOẢN MỚI";
            }

            if (_subtitleText != null)
            {
                _subtitleText.text = isRegister ? "MANGO HEIST - ĐĂNG KÝ TÀI KHOẢN MỚI" : "MANGO HEIST - HỆ THỐNG ĐĂNG NHẬP";
            }
        }

        private void OnSubmitClicked()
        {
            string username = _inputUsername != null ? _inputUsername.text.Trim() : "";
            string password = _inputPassword != null ? _inputPassword.text : "";

            if (_isRegisterMode)
            {
                string email = string.Empty; //_inputEmail != null ? _inputEmail.text.Trim() : "";
                string confirm = _inputConfirmPassword != null ? _inputConfirmPassword.text : "";

                if (AuthManager.Instance != null)
                {
                    AuthManager.Instance.Register(username, email, password, confirm, (success, msg) =>
                    {
                        if (success)
                        {
                            ShowToast("Đăng ký thành công! Đang vào game...", new Color32(16, 185, 129, 255));
                            OnLoginSuccess(AuthManager.Instance.CurrentUser);
                        }
                        else
                        {
                            ShowToast(msg);
                        }
                    });
                }
                else
                {
                    ShowToast(AuthErrorCodes.DB_CONNECT_TIMEOUT);
                }
            }
            else
            {
                if (AuthManager.Instance != null)
                {
                    AuthManager.Instance.Login(username, password, (success, msg) =>
                    {
                        if (success)
                        {
                            OnLoginSuccess(AuthManager.Instance.CurrentUser);
                        }
                        else
                        {
                            ShowToast(msg);
                        }
                    });
                }
                else
                {
                    ShowToast(AuthErrorCodes.DB_CONNECT_TIMEOUT);
                }
            }
        }

        private void OnGuestClicked()
        {
            if (AuthManager.Instance != null)
            {
                AuthManager.Instance.PlayAsGuest((success, msg) =>
                {
                    if (success)
                    {
                        OnLoginSuccess(AuthManager.Instance.CurrentUser);
                    }
                    else
                    {
                        ShowToast(msg);
                    }
                });
            }
        }

        private void OnLoginSuccess(UserAccount account)
        {
            if (_mainCardGo != null) _mainCardGo.SetActive(false);
            if (_profileCardGo != null) _profileCardGo.SetActive(true);

            var saveData = SaveManager.Instance != null ? SaveManager.Instance.CurrentData : null;
            if (_txtWelcome != null) _txtWelcome.text = $"Xin chào, <color=#F59E0B>{account.username}</color>!";
            if (saveData != null)
            {
                if (_txtLevel != null) _txtLevel.text = $"Cấp độ: {saveData.playerLevel} (EXP: {saveData.playerExp})";
                if (_txtCoins != null) _txtCoins.text = $"Xu Xoài: <color=#F59E0B>{saveData.mangoCoins} Xu</color>";
                if (_txtMaps != null) _txtMaps.text = $"Map đã mở: {saveData.unlockedMapIds.Count} map";
            }
        }

        private void OnLogoutClicked()
        {
            if (AuthManager.Instance != null)
            {
                AuthManager.Instance.Logout();
            }

            if (_profileCardGo != null) _profileCardGo.SetActive(false);
            if (_mainCardGo != null) _mainCardGo.SetActive(true);

            if (_inputPassword != null) _inputPassword.text = "";
            if (_inputConfirmPassword != null) _inputConfirmPassword.text = "";

            ShowToast("Đã đăng xuất an toàn.", new Color32(59, 130, 246, 255));
        }

        public void ShowToast(string message, Color32? customBgColor = null)
        {
            if (_toastPopupGo == null || _toastText == null) return;

            if (_toastCoroutine != null)
            {
                StopCoroutine(_toastCoroutine);
            }

            _toastText.text = message;
            var img = _toastPopupGo.GetComponent<Image>();
            if (img != null)
            {
                img.color = customBgColor ?? new Color32(127, 29, 29, 245);
            }

            _toastCoroutine = StartCoroutine(ToastRoutine(3.0f));
        }

        private IEnumerator ToastRoutine(float duration)
        {
            _toastPopupGo.SetActive(true);
            yield return new WaitForSeconds(duration);
            _toastPopupGo.SetActive(false);
            _toastCoroutine = null;
        }

        private void OnPlayClicked()
        {
            if (_isLoadingGame)
                return;

            _isLoadingGame = true;

            // Báo cho PhotonConnectionTest tự kết nối
            // sau khi scene gameplay được tải.
            global::PhotonConnectionTest.AutoStartOnNextScene = true;

            try
            {
                var operation =
                    SceneManager.LoadSceneAsync("NetworkingTest");

                if (operation == null)
                    throw new System.InvalidOperationException(
                        "Không thể tải scene NetworkingTest."
                    );
            }
            catch (System.Exception ex)
            {
                global::PhotonConnectionTest.AutoStartOnNextScene = false;
                _isLoadingGame = false;

                Debug.LogException(ex);

                ShowToast(
                    "Không tải được scene gameplay. Hãy kiểm tra Build Profiles."
                );
            }
        }
    }
}
