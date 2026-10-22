using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MangoHeist.Data;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace MangoHeist.Auth
{
    // Giữ các tên lỗi để LoginUIController hiện tại tiếp tục biên dịch.
    public static class AuthErrorCodes
    {
        public const string DB_CONNECT_TIMEOUT = "Không thể kết nối dịch vụ tài khoản hoặc Cloud Save. Hãy kiểm tra mạng và thử lại.";
        public const string DB_TOO_MANY_CONNECTIONS = "Dịch vụ đang bận. Vui lòng thử lại sau.";
        public const string DB_MAINTENANCE = "Dịch vụ đang bảo trì hoặc chưa được cấu hình.";
        public const string DB_RECORD_NOT_FOUND = "Không tìm thấy hồ sơ người chơi trên Cloud Save.";
        public const string LOGIN_INVALID_CREDENTIALS = "Tên tài khoản hoặc mật khẩu không chính xác.";
        public const string LOGIN_EMPTY_FIELDS = "Vui lòng nhập đầy đủ tên tài khoản và mật khẩu.";
        public const string LOGIN_ACC_BANNED = "Tài khoản bị khóa. Cơ chế khóa tài khoản cần được triển khai ở backend.";
        public const string LOGIN_TOO_MANY_ATTEMPTS = "Bạn thử quá nhiều lần. Vui lòng đợi rồi thử lại.";
        public const string LOGIN_REQUIRE_CAPTCHA = "CAPTCHA chưa được triển khai trong bản thử nghiệm này.";
        public const string REG_USERNAME_EXISTS = "Tên tài khoản đã được sử dụng.";
        public const string REG_INVALID_USERNAME_FORMAT = "Tên tài khoản phải dài 3–20 ký tự, gồm chữ, số hoặc các ký tự . - @ _.";
        public const string REG_WEAK_PASSWORD = "Mật khẩu phải dài 8–30 ký tự, có chữ hoa, chữ thường, số và ký tự đặc biệt.";
        public const string REG_PASSWORD_MISMATCH = "Mật khẩu xác nhận không trùng khớp.";
        public const string REG_EMAIL_EXISTS = "Email chưa được hỗ trợ bởi luồng Username & Password này.";
        public const string REG_RATE_LIMITED = "Yêu cầu đăng ký quá nhanh. Vui lòng thử lại sau.";
    }

    // Model tương thích với LoginUIController cũ. Dữ liệu xác thực thật do UGS quản lý.
    [Serializable]
    public class UserAccount
    {
        public string username;
        public string email;
        public string passwordHash; // Không sử dụng; không lưu mật khẩu/hash ở client.
        public string playerId;
        public string registeredDate;
        public string lastLoginDate;
        public bool isBanned;
        public string banReason;
    }

    public class AuthManager : MonoBehaviour
    {
        private static AuthManager _instance;
        private static Task _servicesInitTask;

        public static AuthManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<AuthManager>();
                    if (_instance == null)
                    {
                        var go = new GameObject("[AuthManager]");
                        _instance = go.AddComponent<AuthManager>();
                    }
                }
                return _instance;
            }
        }

        public UserAccount CurrentUser { get; private set; }
        public bool IsLoggedIn => CurrentUser != null;
        public bool IsGuest { get; private set; }
        public bool IsBusy { get; private set; }

        public event Action<UserAccount> OnLoginSuccess;
        public event Action OnLoggedOut;

        [Header("Simulation Flags (For Testing Only)")]
        public bool simulateNetworkTimeout;
        public bool simulateServerBusy;
        public bool simulateMaintenance;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);
            _ = WarmupServicesAsync();
        }

        // Giữ API cũ để các script khác không bị lỗi compile.
        public void EnsureInitialized() => _ = WarmupServicesAsync();

        private static async Task EnsureServicesInitializedAsync()
        {
            if (_servicesInitTask == null)
                _servicesInitTask = UnityServices.InitializeAsync();

            try
            {
                await _servicesInitTask;
            }
            catch
            {
                _servicesInitTask = null; // Cho phép thử khởi tạo lại sau lỗi mạng/cấu hình.
                throw;
            }
        }

        private async Task WarmupServicesAsync()
        {
            try
            {
                await EnsureServicesInitializedAsync();
                Debug.Log("[AuthManager] Unity Gaming Services đã khởi tạo.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AuthManager] Chưa khởi tạo được Unity Gaming Services: {ex.Message}");
            }
        }

        // Giữ chữ ký cũ để LoginUIController hiện tại có thể gọi.
        // MVP này dùng Username & Password; email bị từ chối thay vì âm thầm giả vờ đã lưu/xác minh.
        public void Register(string username, string email, string password, string confirmPassword, Action<bool, string> callback)
        {
            _ = RegisterAsync(username, email, password, confirmPassword, callback);
        }

        public void Register(string username, string password, Action<bool, string> callback)
        {
            Register(username, string.Empty, password, password, callback);
        }

        private async Task RegisterAsync(string username, string email, string password, string confirmPassword, Action<bool, string> callback)
        {
            if (IsBusy) { callback?.Invoke(false, "Đang xử lý yêu cầu trước đó. Vui lòng chờ."); return; }
            IsBusy = true;
            try
            {
                ThrowIfSimulationEnabled();
                username = username?.Trim() ?? string.Empty;
                email = email?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
                    throw new InvalidOperationException(AuthErrorCodes.LOGIN_EMPTY_FIELDS);
                if (!Regex.IsMatch(username, @"^[a-zA-Z0-9.@_-]{3,20}$"))
                    throw new InvalidOperationException(AuthErrorCodes.REG_INVALID_USERNAME_FORMAT);
                if (password.Length < 8 || password.Length > 30 ||
                    !Regex.IsMatch(password, @"[A-Z]") || !Regex.IsMatch(password, @"[a-z]") ||
                    !Regex.IsMatch(password, @"[0-9]") || !Regex.IsMatch(password, @"[^a-zA-Z0-9]"))
                    throw new InvalidOperationException(AuthErrorCodes.REG_WEAK_PASSWORD);
                if (password != confirmPassword)
                    throw new InvalidOperationException(AuthErrorCodes.REG_PASSWORD_MISMATCH);
                if (!string.IsNullOrEmpty(email))
                    throw new InvalidOperationException("Bản MVP đang dùng Username & Password; trường email cần để trống vì chưa có luồng xác minh email.");

                await EnsureServicesInitializedAsync();
                SignOutIfNeeded();
                await AuthenticationService.Instance.SignUpWithUsernamePasswordAsync(username, password);

                bool completed = await CompleteSessionAsync(username, string.Empty, isGuest: false, callback);
                if (completed)
                    callback?.Invoke(true, "Đăng ký thành công!");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AuthManager] Đăng ký thất bại: {ex}");
                callback?.Invoke(false, ToFriendlyMessage(ex, isRegistration: true));
            }
            finally { IsBusy = false; }
        }

        public void Login(string username, string password, Action<bool, string> callback)
        {
            _ = LoginAsync(username, password, callback);
        }

        private async Task LoginAsync(string username, string password, Action<bool, string> callback)
        {
            if (IsBusy) { callback?.Invoke(false, "Đang xử lý yêu cầu trước đó. Vui lòng chờ."); return; }
            IsBusy = true;
            try
            {
                ThrowIfSimulationEnabled();
                username = username?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
                    throw new InvalidOperationException(AuthErrorCodes.LOGIN_EMPTY_FIELDS);

                await EnsureServicesInitializedAsync();
                SignOutIfNeeded();
                await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(username, password);

                bool completed = await CompleteSessionAsync(username, string.Empty, isGuest: false, callback);
                if (completed)
                    callback?.Invoke(true, "Đăng nhập thành công!");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AuthManager] Đăng nhập thất bại: {ex.Message}");
                callback?.Invoke(false, ToFriendlyMessage(ex, isRegistration: false));
            }
            finally { IsBusy = false; }
        }

        public void PlayAsGuest(Action<bool, string> callback)
        {
            _ = PlayAsGuestAsync(callback);
        }

        private async Task PlayAsGuestAsync(Action<bool, string> callback)
        {
            if (IsBusy) { callback?.Invoke(false, "Đang xử lý yêu cầu trước đó. Vui lòng chờ."); return; }
            IsBusy = true;
            try
            {
                ThrowIfSimulationEnabled();
                await EnsureServicesInitializedAsync();
                SignOutIfNeeded();
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

                string guestName = "Khách" + UnityEngine.Random.Range(1000, 9999);
                bool completed = await CompleteSessionAsync(guestName, string.Empty, isGuest: true, callback);
                if (completed)
                    callback?.Invoke(true, "Đang vào game với tài khoản khách.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AuthManager] Chơi khách thất bại: {ex.Message}");
                callback?.Invoke(false, ToFriendlyMessage(ex, isRegistration: false));
            }
            finally { IsBusy = false; }
        }

        private async Task<bool> CompleteSessionAsync(string username, string email, bool isGuest, Action<bool, string> callback)
        {
            var auth = AuthenticationService.Instance;
            var account = new UserAccount
            {
                username = string.IsNullOrWhiteSpace(username) ? "Người chơi" : username,
                email = email ?? string.Empty,
                passwordHash = string.Empty,
                playerId = auth.PlayerId,
                registeredDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                lastLoginDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                isBanned = false,
                banReason = string.Empty
            };

            CurrentUser = account;
            IsGuest = isGuest;

            string saveLoadError = null;
            bool saveLoaded = await SaveManager.Instance.SetCurrentPlayerAsync(
                account.playerId,
                account.username,
                message => saveLoadError = message);

            if (!saveLoaded)
            {
                CurrentUser = null;
                IsGuest = false;
                try { auth.SignOut(); } catch { }
                callback?.Invoke(false, "Xác thực tài khoản thành công nhưng không tải được hồ sơ Cloud Save. Dữ liệu chưa bị tạo lại hay ghi đè. " + (saveLoadError ?? string.Empty));
                return false;
            }

            OnLoginSuccess?.Invoke(account);
            return true;
        }

        public void Logout() => _ = LogoutAsync();

        private async Task LogoutAsync()
        {
            try
            {
                if (SaveManager.Instance != null && SaveManager.Instance.IsDataLoaded)
                    await SaveManager.Instance.SaveGameAsync();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AuthManager] Không lưu được dữ liệu trước khi đăng xuất: {ex.Message}");
            }
            finally
            {
                try
                {
                    if (AuthenticationService.Instance.IsSignedIn)
                        AuthenticationService.Instance.SignOut();
                }
                catch (Exception ex) { Debug.LogWarning($"[AuthManager] Đăng xuất UGS: {ex.Message}"); }

                CurrentUser = null;
                IsGuest = false;
                if (SaveManager.Instance != null) SaveManager.Instance.ResetSession();
                OnLoggedOut?.Invoke();
            }
        }

        // Các hàm này không thể khóa tài khoản thật từ client. Giữ chữ ký để tránh lỗi tương thích,
        // nhưng việc ban/unban phải được làm ở backend có quyền quản trị.
        public void BanAccount(string username, string reason = "Vi phạm điều khoản")
            => Debug.LogWarning("[AuthManager] BanAccount chỉ là placeholder; hãy triển khai bằng backend, không phải client.");

        public void UnbanAccount(string username)
            => Debug.LogWarning("[AuthManager] UnbanAccount chỉ là placeholder; hãy triển khai bằng backend, không phải client.");

        public void ResetSecurityLimits()
        {
            simulateNetworkTimeout = false;
            simulateServerBusy = false;
            simulateMaintenance = false;
        }

        private void ThrowIfSimulationEnabled()
        {
            if (simulateMaintenance) throw new InvalidOperationException(AuthErrorCodes.DB_MAINTENANCE);
            if (simulateNetworkTimeout) throw new InvalidOperationException(AuthErrorCodes.DB_CONNECT_TIMEOUT);
            if (simulateServerBusy) throw new InvalidOperationException(AuthErrorCodes.DB_TOO_MANY_CONNECTIONS);
        }

        private static void SignOutIfNeeded()
        {
            if (AuthenticationService.Instance.IsSignedIn)
                AuthenticationService.Instance.SignOut();
        }

        private static string ToFriendlyMessage(Exception ex, bool isRegistration)
        {
            if (ex is InvalidOperationException) return ex.Message;
            string message = (ex.Message ?? string.Empty).ToLowerInvariant();
            if (message.Contains("already exists") || message.Contains("already in use") || message.Contains("username is taken"))
                return AuthErrorCodes.REG_USERNAME_EXISTS;
            if (message.Contains("invalid username") || message.Contains("invalid password") || message.Contains("invalid credentials") || message.Contains("authentication failed"))
                return isRegistration ? AuthErrorCodes.REG_WEAK_PASSWORD : AuthErrorCodes.LOGIN_INVALID_CREDENTIALS;
            if (message.Contains("network") || message.Contains("timeout") || message.Contains("connection") || message.Contains("offline"))
                return AuthErrorCodes.DB_CONNECT_TIMEOUT;
            return (isRegistration ? "Đăng ký thất bại: " : "Đăng nhập thất bại: ") + ex.Message;
        }
    }
}
