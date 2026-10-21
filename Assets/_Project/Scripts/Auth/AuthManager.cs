using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using MangoHeist.Data;
using UnityEngine;

namespace MangoHeist.Auth
{
    [Serializable]
    public class UserAccount
    {
        public string username;
        public string passwordHash;
        public string playerId;
        public string registeredDate;
        public string lastLoginDate;
    }

    [Serializable]
    public class AccountsDatabase
    {
        public List<UserAccount> accounts = new List<UserAccount>();
        public UserAccount guestAccount; // Lưu thông tin tài khoản khách để không bị reset khi mở lại game
    }

    /// <summary>
    /// Quản lý hệ thống Đăng nhập / Đăng ký tài khoản người chơi.
    /// - Quản lý cơ sở dữ liệu tài khoản (Database cục bộ, sẵn sàng mở rộng lên Firebase/UGS).
    /// - Mỗi tài khoản có tiến trình lưu riêng (Map đã mở, số xu, skin).
    /// - Hỗ trợ chế độ "Chơi Khách / Offline" (Không cần mật khẩu).
    /// </summary>
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
                    if (existing != null)
                    {
                        _instance = existing;
                    }
                    else
                    {
                        var go = new GameObject("[AuthManager]");
                        _instance = go.AddComponent<AuthManager>();
                        if (Application.isPlaying)
                        {
                            DontDestroyOnLoad(go);
                        }
                    }
                    _instance.EnsureInitialized();
                }
                else
                {
                    _instance.EnsureInitialized();
                }
                return _instance;
            }
        }

        public void EnsureInitialized()
        {
            if (_db == null)
            {
                LoadAccountsDatabase();
            }
            if (_db == null) _db = new AccountsDatabase();
            if (_db.accounts == null) _db.accounts = new List<UserAccount>();
        }

        private string DatabaseFilePath => Path.Combine(Application.persistentDataPath, "accounts_db.json");

        private AccountsDatabase _db;
        public UserAccount CurrentUser { get; private set; }
        public bool IsLoggedIn => CurrentUser != null;

        public event Action<UserAccount> OnLoginSuccess;
        public event Action OnLoggedOut;

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                if (Application.isPlaying)
                {
                    DontDestroyOnLoad(gameObject);
                }
                LoadAccountsDatabase();
            }
            else if (_instance != this)
            {
                if (Application.isPlaying)
                {
                    Destroy(gameObject);
                }
                else
                {
                    DestroyImmediate(gameObject);
                }
            }
        }

        private void LoadAccountsDatabase()
        {
            try
            {
                if (File.Exists(DatabaseFilePath))
                {
                    string json = File.ReadAllText(DatabaseFilePath);
                    _db = JsonUtility.FromJson<AccountsDatabase>(json) ?? new AccountsDatabase();
                }
                else
                {
                    _db = new AccountsDatabase();
                    SaveAccountsDatabase();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AuthManager] Lỗi đọc Database tài khoản: {ex.Message}");
                _db = new AccountsDatabase();
            }

            if (_db == null) _db = new AccountsDatabase();
            if (_db.accounts == null) _db.accounts = new List<UserAccount>();
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
        /// Đăng ký tài khoản mới
        /// </summary>
        public void Register(string username, string password, Action<bool, string> callback)
        {
            EnsureInitialized();
            username = username?.Trim();
            if (string.IsNullOrEmpty(username) || username.Length < 3)
            {
                callback?.Invoke(false, "Tên tài khoản phải có ít nhất 3 ký tự!");
                return;
            }

            if (username.Length > 16)
            {
                callback?.Invoke(false, "Tên tài khoản tối đa 16 ký tự!");
                return;
            }

            // Kiểm tra ký tự hợp lệ, chống phá hoại JSON hoặc SQL Injection
            if (!System.Text.RegularExpressions.Regex.IsMatch(username, @"^[a-zA-Z0-9_ -]+$"))
            {
                callback?.Invoke(false, "Tên tài khoản chỉ được chứa chữ cái, số, gạch dưới hoặc khoảng trắng!");
                return;
            }

            if (string.IsNullOrEmpty(password) || password.Length < 4)
            {
                callback?.Invoke(false, "Mật khẩu phải có ít nhất 4 ký tự!");
                return;
            }

            // Kiểm tra xem tên tài khoản đã tồn tại chưa
            var existing = _db.accounts.Find(a => a.username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                callback?.Invoke(false, "Tên tài khoản này đã có người sử dụng!");
                return;
            }

            // Tạo tài khoản mới với mật khẩu mã hóa SHA-256
            var newAccount = new UserAccount
            {
                username = username,
                passwordHash = HashPassword(password),
                playerId = "user_" + Guid.NewGuid().ToString().Substring(0, 8),
                registeredDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                lastLoginDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            _db.accounts.Add(newAccount);
            SaveAccountsDatabase();

            Debug.Log($"<color=#4CAF50><b>[AuthManager]</b> Đăng ký thành công tài khoản:</color> {username} (ID: {newAccount.playerId})");
            
            // Tự động đăng nhập luôn sau khi đăng ký
            CurrentUser = newAccount;
            SyncWithSaveManager(newAccount);
            OnLoginSuccess?.Invoke(newAccount);
            callback?.Invoke(true, "Đăng ký thành công!");
        }

        /// <summary>
        /// Đăng nhập tài khoản
        /// </summary>
        public void Login(string username, string password, Action<bool, string> callback)
        {
            EnsureInitialized();
            username = username?.Trim();
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                callback?.Invoke(false, "Vui lòng nhập đầy đủ tài khoản và mật khẩu!");
                return;
            }

            var account = _db.accounts.Find(a => a.username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (account == null)
            {
                callback?.Invoke(false, "Tài khoản không tồn tại! Hãy bấm Đăng ký.");
                return;
            }

            string inputHash = HashPassword(password);
            if (account.passwordHash != inputHash)
            {
                callback?.Invoke(false, "Mật khẩu không chính xác!");
                return;
            }

            account.lastLoginDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            SaveAccountsDatabase();

            CurrentUser = account;
            SyncWithSaveManager(account);

            Debug.Log($"<color=#2196F3><b>[AuthManager]</b> Đăng nhập thành công:</color> {username}");
            OnLoginSuccess?.Invoke(account);
            callback?.Invoke(true, "Đăng nhập thành công!");
        }

        /// <summary>
        /// Chơi nhanh dạng Khách (Guest / Offline) - Tự động bảo lưu tiến trình trên máy
        /// </summary>
        public void PlayAsGuest(Action<bool, string> callback)
        {
            EnsureInitialized();
            if (_db.guestAccount == null || string.IsNullOrEmpty(_db.guestAccount.playerId))
            {
                _db.guestAccount = new UserAccount
                {
                    username = "Khách_" + UnityEngine.Random.Range(1000, 9999),
                    playerId = "guest_local_player",
                    registeredDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    lastLoginDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };
                SaveAccountsDatabase();
            }
            else
            {
                _db.guestAccount.lastLoginDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                SaveAccountsDatabase();
            }

            CurrentUser = _db.guestAccount;
            SyncWithSaveManager(_db.guestAccount);

            Debug.Log($"<color=#FF9800><b>[AuthManager]</b> Nạp lại dữ liệu Khách:</color> {_db.guestAccount.username} ({_db.guestAccount.playerId})");
            OnLoginSuccess?.Invoke(_db.guestAccount);
            callback?.Invoke(true, "Đang vào game với tài khoản Khách...");
        }

        /// <summary>
        /// Đăng xuất tài khoản
        /// </summary>
        public void Logout()
        {
            CurrentUser = null;
            if (SaveManager.Instance != null)
            {
                SaveManager.Instance.ResetSession();
            }
            OnLoggedOut?.Invoke();
            Debug.Log("[AuthManager] Đã đăng xuất.");
        }

        private void SyncWithSaveManager(UserAccount account)
        {
            if (SaveManager.Instance != null)
            {
                SaveManager.Instance.SetCurrentPlayer(account.playerId, account.username);
            }
        }

        private string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password + "_MangoHeistSalt_2026"));
                StringBuilder builder = new StringBuilder();
                foreach (byte b in bytes)
                {
                    builder.Append(b.ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
}
