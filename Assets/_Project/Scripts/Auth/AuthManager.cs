using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MangoHeist.Data;
using UnityEngine;

namespace MangoHeist.Auth
{
    /// <summary>
    /// Danh mục toàn bộ các thông báo lỗi chuẩn UX theo đặc tả của hệ thống Mango Heist
    /// </summary>
    public static class AuthErrorCodes
    {
        // A. Lỗi Hệ thống / Cơ sở dữ liệu (Database Errors)
        public const string DB_CONNECT_TIMEOUT = "Lỗi: Không thể kết nối với máy chủ. Vui lòng kiểm tra lại mạng hoặc thử lại sau! (Mã lỗi: DB_CONNECT_TIMEOUT)";
        public const string DB_TOO_MANY_CONNECTIONS = "Hệ thống đang bận do lượng người truy cập quá đông. Vui lòng quay lại sau vài phút! (Mã lỗi: DB_TOO_MANY_CONNECTIONS)";
        public const string DB_MAINTENANCE = "Hệ thống đang bảo trì cơ sở dữ liệu để nâng cấp. Thời gian dự kiến: 02:00 - 04:00 (Mã lỗi: DB_MAINTENANCE)";
        public const string DB_RECORD_NOT_FOUND = "Lỗi: Không thể truy xuất dữ liệu tài khoản của bạn. Vui lòng liên hệ hỗ trợ! (Mã lỗi: DB_RECORD_NOT_FOUND)";

        // B. Lỗi tại Menu Đăng nhập (Login Errors)
        public const string LOGIN_INVALID_CREDENTIALS = "Tên tài khoản hoặc mật khẩu không chính xác. Vui lòng kiểm tra lại!";
        public const string LOGIN_EMPTY_FIELDS = "Vui lòng nhập đầy đủ tên tài khoản và mật khẩu!";
        public const string LOGIN_ACC_BANNED = "Tài khoản của bạn đã bị tạm khóa do vi phạm điều khoản hoặc nghi ngờ gian lận. (Mã lỗi: ACC_BANNED)";
        public const string LOGIN_TOO_MANY_ATTEMPTS = "Bạn đã nhập sai mật khẩu quá nhiều lần. Vui lòng thử lại sau 5 phút!";
        public const string LOGIN_REQUIRE_CAPTCHA = "Vui lòng hoàn thành xác thực mã CAPTCHA để tiếp tục!";

        // C. Lỗi tại Menu Đăng ký (Registration Errors)
        public const string REG_USERNAME_EXISTS = "Tên tài khoản này đã được sử dụng. Vui lòng chọn tên khác!";
        public const string REG_INVALID_USERNAME_FORMAT = "Tên tài khoản phải từ 6-24 ký tự, chỉ gồm chữ cái và số (không khoảng trắng, không ký tự đặc biệt)!";
        public const string REG_WEAK_PASSWORD = "Mật khẩu phải chứa ít nhất 8 ký tự, bao gồm cả chữ hoa, chữ thường và số!";
        public const string REG_PASSWORD_MISMATCH = "Mật khẩu xác nhận không trùng khớp. Vui lòng nhập lại!";
        public const string REG_EMAIL_EXISTS = "Email hoặc số điện thoại này đã được liên kết với một tài khoản khác!";
        public const string REG_RATE_LIMITED = "Yêu cầu đăng ký quá nhanh. Vui lòng đợi một lát trước khi thử lại!";
    }

    [Serializable]
    public class UserAccount
    {
        public string username;
        public string email;
        public string passwordHash;
        public string playerId;
        public string registeredDate;
        public string lastLoginDate;
        public bool isBanned = false;
        public string banReason = "";
    }

    [Serializable]
    public class AccountsDatabase
    {
        public List<UserAccount> accounts = new List<UserAccount>();
        public UserAccount guestAccount;
    }

    public class AuthManager : MonoBehaviour
    {
        private static AuthManager _instance;
        public static AuthManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var existing = FindAnyObjectByType<AuthManager>();
                    if (existing != null) _instance = existing;
                    else
                    {
                        var go = new GameObject("[AuthManager]");
                        _instance = go.AddComponent<AuthManager>();
                        if (Application.isPlaying) DontDestroyOnLoad(go);
                    }
                }
                _instance.EnsureInitialized();
                return _instance;
            }
        }

        public static string DatabaseFilePath => Path.Combine(Application.persistentDataPath, "accounts_db.json");

        private AccountsDatabase _db;
        public UserAccount CurrentUser { get; private set; }
        public bool IsLoggedIn => CurrentUser != null;
        public bool IsGuest => CurrentUser != null && CurrentUser.playerId == "guest_local_player";

        public event Action<UserAccount> OnLoginSuccess;
        public event Action OnLoggedOut;

        // Mô phỏng sự cố mạng/máy chủ
        [Header("Simulation Flags (For Testing)")]
        public bool simulateNetworkTimeout = false;
        public bool simulateServerBusy = false;
        public bool simulateMaintenance = false;

        // Cơ chế bảo mật: Rate Limit & Khóa đăng nhập sai
        private float _lastRegisterTimestamp = -10f;
        private readonly Dictionary<string, int> _failedAttemptsMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _lockoutMap = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                if (Application.isPlaying) DontDestroyOnLoad(gameObject);
                EnsureInitialized();
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }

        public void EnsureInitialized()
        {
            if (_db != null) return;

            try
            {
                if (File.Exists(DatabaseFilePath))
                {
                    string json = File.ReadAllText(DatabaseFilePath);
                    _db = JsonUtility.FromJson<AccountsDatabase>(json);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AuthManager] File Database lỗi: {ex.Message}. Khởi tạo Database mới.");
            }

            if (_db == null)
            {
                _db = new AccountsDatabase();
                SaveAccountsDatabase();
            }
        }

        private void SaveAccountsDatabase()
        {
            try
            {
                string json = JsonUtility.ToJson(_db, true);
                File.WriteAllText(DatabaseFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AuthManager] Lỗi lưu Database tài khoản: {ex.Message}");
            }
        }

        /// <summary>
        /// Đăng ký tài khoản đầy đủ (với Email và Xác nhận mật khẩu từ Form UI)
        /// </summary>
        public void Register(string username, string email, string password, string confirmPassword, Action<bool, string> callback)
        {
            // A. Kiểm tra mô phỏng lỗi Hệ thống / Cơ sở dữ liệu
            if (simulateMaintenance)
            {
                callback?.Invoke(false, AuthErrorCodes.DB_MAINTENANCE);
                return;
            }
            if (simulateNetworkTimeout)
            {
                callback?.Invoke(false, AuthErrorCodes.DB_CONNECT_TIMEOUT);
                return;
            }
            if (simulateServerBusy)
            {
                callback?.Invoke(false, AuthErrorCodes.DB_TOO_MANY_CONNECTIONS);
                return;
            }

            EnsureInitialized();
            username = username?.Trim();
            email = email?.Trim();

            // B.2. Bỏ trống trường thông tin
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                callback?.Invoke(false, AuthErrorCodes.LOGIN_EMPTY_FIELDS);
                return;
            }

            // C.2. Định dạng tài khoản không hợp lệ: 6-24 ký tự, chỉ gồm chữ cái và số
            if (!Regex.IsMatch(username, @"^[a-zA-Z0-9]{6,24}$"))
            {
                callback?.Invoke(false, AuthErrorCodes.REG_INVALID_USERNAME_FORMAT);
                return;
            }

            // C.3. Mật khẩu quá yếu: ít nhất 8 ký tự, gồm cả chữ hoa, chữ thường và số
            if (string.IsNullOrEmpty(password) || password.Length < 8 ||
                !Regex.IsMatch(password, @"[A-Z]") ||
                !Regex.IsMatch(password, @"[a-z]") ||
                !Regex.IsMatch(password, @"[0-9]"))
            {
                callback?.Invoke(false, AuthErrorCodes.REG_WEAK_PASSWORD);
                return;
            }

            // C.4. Mật khẩu xác nhận không trùng khớp
            if (password != confirmPassword)
            {
                callback?.Invoke(false, AuthErrorCodes.REG_PASSWORD_MISMATCH);
                return;
            }

            // C.1. Tài khoản đã tồn tại
            var existing = _db.accounts.Find(a => a.username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                callback?.Invoke(false, AuthErrorCodes.REG_USERNAME_EXISTS);
                return;
            }

            // C.5. Email đã được đăng ký
            if (!string.IsNullOrEmpty(email))
            {
                var existingEmail = _db.accounts.Find(a => !string.IsNullOrEmpty(a.email) && a.email.Equals(email, StringComparison.OrdinalIgnoreCase));
                if (existingEmail != null)
                {
                    callback?.Invoke(false, AuthErrorCodes.REG_EMAIL_EXISTS);
                    return;
                }
            }

            // C.6. Kiểm tra Spam / Đăng ký quá nhanh (chỉ kiểm tra khi thông tin đã hợp lệ)
            float now = Time.realtimeSinceStartup;
            if (now - _lastRegisterTimestamp < 3.0f)
            {
                callback?.Invoke(false, AuthErrorCodes.REG_RATE_LIMITED);
                return;
            }
            _lastRegisterTimestamp = now;

            // Đăng ký thành công
            var newAccount = new UserAccount
            {
                username = username,
                email = email ?? "",
                passwordHash = HashPassword(password),
                playerId = "user_" + Guid.NewGuid().ToString().Substring(0, 8),
                registeredDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                lastLoginDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            _db.accounts.Add(newAccount);
            SaveAccountsDatabase();

            CurrentUser = newAccount;
            SyncWithSaveManager(newAccount);
            OnLoginSuccess?.Invoke(newAccount);
            callback?.Invoke(true, "Đăng ký thành công!");
        }

        /// <summary>
        /// Quá tải Register rút gọn (hỗ trợ các bài test và API gọi nhanh)
        /// </summary>
        public void Register(string username, string password, Action<bool, string> callback)
        {
            Register(username, "", password, password, callback);
        }

        /// <summary>
        /// Đăng nhập tài khoản với đầy đủ kịch bản bảo mật và xử lý lỗi
        /// </summary>
        public void Login(string username, string password, Action<bool, string> callback)
        {
            // A. Kiểm tra mô phỏng lỗi Hệ thống / Cơ sở dữ liệu
            if (simulateMaintenance)
            {
                callback?.Invoke(false, AuthErrorCodes.DB_MAINTENANCE);
                return;
            }
            if (simulateNetworkTimeout)
            {
                callback?.Invoke(false, AuthErrorCodes.DB_CONNECT_TIMEOUT);
                return;
            }
            if (simulateServerBusy)
            {
                callback?.Invoke(false, AuthErrorCodes.DB_TOO_MANY_CONNECTIONS);
                return;
            }

            EnsureInitialized();
            username = username?.Trim();

            // B.2. Bỏ trống trường thông tin
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                callback?.Invoke(false, AuthErrorCodes.LOGIN_EMPTY_FIELDS);
                return;
            }

            // B.4. Nhập sai quá nhiều lần (khóa 5 phút)
            if (_lockoutMap.TryGetValue(username, out DateTime lockoutUntil) && DateTime.Now < lockoutUntil)
            {
                callback?.Invoke(false, AuthErrorCodes.LOGIN_TOO_MANY_ATTEMPTS);
                return;
            }

            var account = _db.accounts.Find(a => a.username.Equals(username, StringComparison.OrdinalIgnoreCase));

            // B.1. Kiểm tra tài khoản hoặc mật khẩu (Bảo mật: Không chỉ rõ sai username hay password)
            if (account == null || account.passwordHash != HashPassword(password))
            {
                int attempts = _failedAttemptsMap.ContainsKey(username) ? _failedAttemptsMap[username] + 1 : 1;
                _failedAttemptsMap[username] = attempts;

                if (attempts >= 5)
                {
                    _lockoutMap[username] = DateTime.Now.AddMinutes(5);
                    callback?.Invoke(false, AuthErrorCodes.LOGIN_TOO_MANY_ATTEMPTS);
                    return;
                }
                else if (attempts >= 3)
                {
                    // B.5. Yêu cầu CAPTCHA khi sai từ 3 lần trở lên
                    callback?.Invoke(false, AuthErrorCodes.LOGIN_REQUIRE_CAPTCHA);
                    return;
                }

                callback?.Invoke(false, AuthErrorCodes.LOGIN_INVALID_CREDENTIALS);
                return;
            }

            // B.3. Tài khoản bị khóa (ACC_BANNED)
            if (account.isBanned)
            {
                callback?.Invoke(false, AuthErrorCodes.LOGIN_ACC_BANNED);
                return;
            }

            // Đăng nhập hợp lệ: Reset bộ đếm số lần sai
            _failedAttemptsMap.Remove(username);
            _lockoutMap.Remove(username);

            account.lastLoginDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            SaveAccountsDatabase();

            CurrentUser = account;
            SyncWithSaveManager(account);
            OnLoginSuccess?.Invoke(account);
            callback?.Invoke(true, "Đăng nhập thành công!");
        }

        public void PlayAsGuest(Action<bool, string> callback)
        {
            if (simulateMaintenance)
            {
                callback?.Invoke(false, AuthErrorCodes.DB_MAINTENANCE);
                return;
            }
            if (simulateNetworkTimeout)
            {
                callback?.Invoke(false, AuthErrorCodes.DB_CONNECT_TIMEOUT);
                return;
            }
            if (simulateServerBusy)
            {
                callback?.Invoke(false, AuthErrorCodes.DB_TOO_MANY_CONNECTIONS);
                return;
            }

            EnsureInitialized();
            if (_db.guestAccount == null || string.IsNullOrEmpty(_db.guestAccount.playerId))
            {
                _db.guestAccount = new UserAccount
                {
                    username = "Khách" + UnityEngine.Random.Range(1000, 9999),
                    playerId = "guest_local_player",
                    registeredDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    lastLoginDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };
                SaveAccountsDatabase();
            }

            CurrentUser = _db.guestAccount;
            SyncWithSaveManager(_db.guestAccount);
            OnLoginSuccess?.Invoke(_db.guestAccount);
            callback?.Invoke(true, "Đang vào game với tài khoản Khách...");
        }

        public void Logout()
        {
            CurrentUser = null;
            if (SaveManager.Instance != null) SaveManager.Instance.ResetSession();
            OnLoggedOut?.Invoke();
        }

        public void BanAccount(string username, string reason = "Vi phạm điều khoản")
        {
            EnsureInitialized();
            var acc = _db.accounts.Find(a => a.username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (acc != null)
            {
                acc.isBanned = true;
                acc.banReason = reason;
                SaveAccountsDatabase();
            }
        }

        public void UnbanAccount(string username)
        {
            EnsureInitialized();
            var acc = _db.accounts.Find(a => a.username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (acc != null)
            {
                acc.isBanned = false;
                acc.banReason = "";
                SaveAccountsDatabase();
            }
        }

        public void ResetSecurityLimits()
        {
            _failedAttemptsMap.Clear();
            _lockoutMap.Clear();
            _lastRegisterTimestamp = -10f;
            simulateNetworkTimeout = false;
            simulateServerBusy = false;
            simulateMaintenance = false;
        }

        private void SyncWithSaveManager(UserAccount account)
        {
            if (SaveManager.Instance == null) return;

            SaveManager.Instance.SetCurrentPlayer(account.playerId, account.username);
            SaveManager.Instance.LoadGame();

            if (SaveManager.Instance.CurrentData == null)
            {
                SaveManager.Instance.NewGame(account.playerId, account.username);
            }
        }

        private static string HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password)) return "";
            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes("MangoSalt_" + password));
                var sb = new StringBuilder();
                foreach (byte b in bytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}
