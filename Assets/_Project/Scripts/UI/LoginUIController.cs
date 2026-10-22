using System.Collections;
using MangoHeist.Auth;
using MangoHeist.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
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

            // ══════════════════════════════════════════════════════════════════
            // TẢI TOÀN BỘ SPRITE ĐỒ HỌA THẬT QUA HELPER BẢO ĐẢM LOAD CHUẨN XÁC
            // ══════════════════════════════════════════════════════════════════
            var spBg = LoadUISprite("UI_Background_Garden");
            var spTitle = LoadUISprite("UI_Title_Logo");
            var spCard = LoadUISprite("UI_MainCard_BambooFrame");
            var spSlot = LoadUISprite("UI_Input_Slot");
            var spBtnGreen = LoadUISprite("UI_Btn_Green");
            var spBtnBlue = LoadUISprite("UI_Btn_Blue");
            var spBtnGrey = LoadUISprite("UI_Btn_Grey");

            // 1. NỀN VƯỜN XOÀI FULL MÀN HÌNH
            _backgroundGo = new GameObject("Background", typeof(RectTransform));
            _backgroundGo.layer = uiLayer;
            _backgroundGo.transform.SetParent(transform, false);
            var bgRt = _backgroundGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            var bgImg = _backgroundGo.AddComponent<Image>();
            if (spBg != null)
            {
                bgImg.sprite = spBg;
                bgImg.color = Color.white;
            }
            else
            {
                bgImg.sprite = null;
                bgImg.color = new Color32(17, 24, 39, 255); // Fallback #111827
            }

            // ══════════════════════════════════════════════════════════════════
            // 2. KHUNG CHÍNH MAINCARD (KHUNG TRE NỨA VIỀN LÁ VÀ QUẢ XOÀI)
            // ══════════════════════════════════════════════════════════════════
            _mainCardGo = new GameObject("MainCard", typeof(RectTransform));
            _mainCardGo.layer = uiLayer;
            _mainCardGo.transform.SetParent(transform, false);
            var cardRt = _mainCardGo.GetComponent<RectTransform>();
            cardRt.anchorMin = new Vector2(0.5f, 0.5f);
            cardRt.anchorMax = new Vector2(0.5f, 0.5f);
            cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.sizeDelta = new Vector2(490, 580);
            cardRt.anchoredPosition = new Vector2(0, -25);

            var cardImg = _mainCardGo.AddComponent<Image>();
            if (spCard != null)
            {
                cardImg.sprite = spCard;
                cardImg.type = Image.Type.Sliced;
                cardImg.color = Color.white;
            }
            else
            {
                cardImg.sprite = null;
                cardImg.color = new Color32(31, 41, 55, 255);
                var cardOutline = _mainCardGo.AddComponent<Outline>();
                cardOutline.effectColor = new Color32(55, 65, 81, 255);
                cardOutline.effectDistance = new Vector2(2, -2);
            }

            var cardShadow = _mainCardGo.AddComponent<Shadow>();
            cardShadow.effectColor = new Color32(0, 0, 0, 160);
            cardShadow.effectDistance = new Vector2(0, -10);

            // Cấu hình VerticalLayoutGroup căn chỉnh lọt lòng khung tre
            var vlg = _mainCardGo.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(48, 48, 40, 42);
            vlg.spacing = 13;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;

            // ══════════════════════════════════════════════════════════════════
            // 3. TIÊU ĐỀ LOGO GAME 3D & SUBTITLE
            // ══════════════════════════════════════════════════════════════════
            if (spTitle != null)
            {
                var logoGo = new GameObject("Title_Logo", typeof(RectTransform));
                logoGo.layer = uiLayer;
                logoGo.transform.SetParent(_mainCardGo.transform, false);
                var le = logoGo.AddComponent<LayoutElement>();
                le.preferredHeight = 110f;
                le.flexibleWidth = 1f;

                var logoImg = logoGo.AddComponent<Image>();
                logoImg.sprite = spTitle;
                logoImg.preserveAspect = true;
                logoImg.color = Color.white;
            }
            else
            {
                CreateTextElement("Title", _mainCardGo.transform, "TRỘM XOÀI ĐẠI CHIẾN", 28, FontStyles.Bold, new Color32(245, 158, 11, 255), 45);
            }

            _subtitleText = CreateTextElement("Subtitle", _mainCardGo.transform, "MANGO HEIST - HỆ THỐNG ĐĂNG NHẬP", 13, FontStyles.Bold, new Color32(254, 240, 138, 255), 22);

            // ══════════════════════════════════════════════════════════════════
            // 4. CÁC Ô NHẬP LIỆU (KHUNG RÃNH GỖ KHẮC CHÌM)
            // ══════════════════════════════════════════════════════════════════
            _inputUsername = CreateInputField("Input_Username", _mainCardGo.transform, "Nhập tên tài khoản (6-24 ký tự)...", false, spSlot, out _);
            _inputEmail = CreateInputField("Input_Email", _mainCardGo.transform, "Nhập email (tùy chọn)...", false, spSlot, out _rowEmail);
            _inputPassword = CreateInputField("Input_Password", _mainCardGo.transform, "Nhập mật khẩu (từ 8 ký tự)...", true, spSlot, out _);
            _inputConfirmPassword = CreateInputField("Input_ConfirmPassword", _mainCardGo.transform, "Nhập lại mật khẩu...", true, spSlot, out _rowConfirmPassword);

            // ══════════════════════════════════════════════════════════════════
            // 5. CÁC NÚT BẤM (BỘ NÚT 3 MÀU: XANH LÁ, XANH DƯƠNG, XÁM ĐÁ)
            // ══════════════════════════════════════════════════════════════════
            var btnLogin = CreateButton("Btn_Login", _mainCardGo.transform, "ĐĂNG NHẬP", spBtnGreen, new Color32(16, 185, 129, 255), 52, out _lblLoginBtn);
            btnLogin.onClick.AddListener(OnSubmitClicked);

            var btnSwitch = CreateButton("Btn_SwitchMode", _mainCardGo.transform, "ĐĂNG KÝ TÀI KHOẢN MỚI", spBtnBlue, new Color32(59, 130, 246, 255), 48, out _lblSwitchModeBtn);
            btnSwitch.onClick.AddListener(() => SetRegisterMode(!_isRegisterMode));

            var btnGuest = CreateButton("Btn_Guest", _mainCardGo.transform, "CHƠI KHÁCH / OFFLINE (BỎ QUA)", spBtnGrey, new Color32(75, 85, 99, 255), 44, out _);
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
            var spCard = LoadUISprite("UI_MainCard_BambooFrame");
            var spBtnGreen = LoadUISprite("UI_Btn_Green");
            var spBtnGrey = LoadUISprite("UI_Btn_Grey");

            int uiLayer = LayerMask.NameToLayer("UI");
            _profileCardGo = new GameObject("ProfileCard", typeof(RectTransform));
            _profileCardGo.layer = uiLayer;
            _profileCardGo.transform.SetParent(transform, false);
            var pRt = _profileCardGo.GetComponent<RectTransform>();
            pRt.anchorMin = new Vector2(0.5f, 0.5f);
            pRt.anchorMax = new Vector2(0.5f, 0.5f);
            pRt.pivot = new Vector2(0.5f, 0.5f);
            pRt.sizeDelta = new Vector2(490, 520);
            pRt.anchoredPosition = new Vector2(0, -20);

            var pImg = _profileCardGo.AddComponent<Image>();
            if (spCard != null)
            {
                pImg.sprite = spCard;
                pImg.type = Image.Type.Sliced;
                pImg.color = Color.white;
            }
            else
            {
                pImg.color = new Color32(31, 41, 55, 255);
                var pOut = _profileCardGo.AddComponent<Outline>();
                pOut.effectColor = new Color32(16, 185, 129, 255);
                pOut.effectDistance = new Vector2(2, -2);
            }

            var vlg = _profileCardGo.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(48, 48, 45, 42);
            vlg.spacing = 14;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;

            CreateTextElement("P_Title", _profileCardGo.transform, "MANGO HEIST - HỒ SƠ", 24, FontStyles.Bold, new Color32(245, 158, 11, 255), 40);
            _txtWelcome = CreateTextElement("P_Welcome", _profileCardGo.transform, "Xin chào người chơi!", 18, FontStyles.Bold, Color.white, 30);
            _txtLevel = CreateTextElement("P_Level", _profileCardGo.transform, "Cấp độ: 1", 16, FontStyles.Normal, new Color32(254, 240, 138, 255), 26);
            _txtCoins = CreateTextElement("P_Coins", _profileCardGo.transform, "Xu Xoài: 100 Xu", 18, FontStyles.Bold, new Color32(245, 158, 11, 255), 30);
            _txtMaps = CreateTextElement("P_Maps", _profileCardGo.transform, "Map đã mở: 1 map", 16, FontStyles.Normal, new Color32(16, 185, 129, 255), 26);

            var btnLobby = CreateButton("Btn_Lobby", _profileCardGo.transform, "VÀO SẢNH CHỜ (LOBBY)", spBtnGreen, new Color32(16, 185, 129, 255), 52, out _);
            btnLobby.onClick.AddListener(() => ShowToast("Chức năng Multiplayer đang phát triển!", new Color32(59, 130, 246, 255)));

            var btnLogout = CreateButton("Btn_Logout", _profileCardGo.transform, "ĐĂNG XUẤT", spBtnGrey, new Color32(239, 68, 68, 255), 44, out _);
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

        private TMP_InputField CreateInputField(string name, Transform parent, string placeholderText, bool isPassword, Sprite slotSprite, out GameObject rowGo)
        {
            int uiLayer = LayerMask.NameToLayer("UI");
            rowGo = new GameObject(name, typeof(RectTransform));
            rowGo.layer = uiLayer;
            rowGo.transform.SetParent(parent, false);

            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = 52f;
            le.flexibleWidth = 1f;

            var rowImg = rowGo.AddComponent<Image>();
            if (slotSprite != null)
            {
                rowImg.sprite = slotSprite;
                rowImg.type = Image.Type.Sliced;
                rowImg.color = Color.white;
            }
            else
            {
                rowImg.sprite = null;
                rowImg.color = new Color32(55, 65, 81, 255);
                var outline = rowGo.AddComponent<Outline>();
                outline.effectColor = new Color32(75, 85, 99, 255);
                outline.effectDistance = new Vector2(1, -1);
            }

            // Text Area Container với RectMask2D (cách lề trái 22px, lề phải 22px, trên dưới 6px)
            var textAreaGo = new GameObject("Text Area", typeof(RectTransform));
            textAreaGo.layer = uiLayer;
            textAreaGo.transform.SetParent(rowGo.transform, false);
            var areaRt = textAreaGo.GetComponent<RectTransform>();
            areaRt.anchorMin = Vector2.zero;
            areaRt.anchorMax = Vector2.one;
            areaRt.offsetMin = new Vector2(22, 6);
            areaRt.offsetMax = new Vector2(-22, -6);
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
            txt.color = new Color32(255, 251, 235, 255); // #FFFBEB warm cream ivory
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
            ph.fontSize = 15f;
            ph.fontStyle = FontStyles.Italic;
            ph.color = new Color32(209, 167, 117, 210); // #D1A775 golden amber placeholder
            ph.alignment = TextAlignmentOptions.MidlineLeft;
            ph.enableWordWrapping = false;
            ph.overflowMode = TextOverflowModes.Overflow;

            var inputField = rowGo.AddComponent<TMP_InputField>();
            inputField.textViewport = areaRt;
            inputField.textComponent = txt;
            inputField.placeholder = ph;
            inputField.selectionColor = new Color32(245, 158, 11, 100);
            
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

        private Button CreateButton(string name, Transform parent, string label, Sprite btnSprite, Color fallbackColor, float height, out TextMeshProUGUI labelTmp)
        {
            var btnGo = new GameObject(name, typeof(RectTransform));
            btnGo.layer = LayerMask.NameToLayer("UI");
            btnGo.transform.SetParent(parent, false);

            var le = btnGo.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleWidth = 1f;

            var img = btnGo.AddComponent<Image>();
            if (btnSprite != null)
            {
                img.sprite = btnSprite;
                img.type = Image.Type.Sliced;
                img.color = Color.white;
            }
            else
            {
                img.sprite = null;
                img.color = fallbackColor;
            }

            var btn = btnGo.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
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

            var shadow = txtGo.AddComponent<Shadow>();
            shadow.effectColor = new Color32(0, 0, 0, 180);
            shadow.effectDistance = new Vector2(1, -2);

            return btn;
        }

        public void SetRegisterMode(bool isRegister)
        {
            _isRegisterMode = isRegister;
            if (_rowEmail != null)
            {
                _rowEmail.SetActive(isRegister);
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
                    rt.sizeDelta = new Vector2(490, isRegister ? 680 : 580);
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
                _subtitleText.text = isRegister ? "ĐĂNG KÝ TÀI KHOẢN MỚI" : "HỆ THỐNG ĐĂNG NHẬP";
            }
        }

        private void OnSubmitClicked()
        {
            string username = _inputUsername != null ? _inputUsername.text.Trim() : "";
            string password = _inputPassword != null ? _inputPassword.text : "";

            if (_isRegisterMode)
            {
                string email = _inputEmail != null ? _inputEmail.text.Trim() : "";
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

        private static Sprite LoadUISprite(string assetName)
        {
#if UNITY_EDITOR
            var sp = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/_Project/Textures/UI/{assetName}.png");
            if (sp == null)
                sp = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/_Project/Textures/UI/{assetName}.jpg");
            if (sp == null)
                sp = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/_Project/Resources/UI/{assetName}.png");
            if (sp == null)
                sp = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/_Project/Resources/UI/{assetName}.jpg");
            if (sp != null) return sp;
#endif
            return Resources.Load<Sprite>($"UI/{assetName}");
        }
    }
}
