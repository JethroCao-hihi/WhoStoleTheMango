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
    [Serializable]
    public class UserAccount
    {
        public string username;
        public string email;
        public string passwordHash;
        public string playerId;
        public string registeredDate;
        public string lastLoginDate;
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
                if (Application.isPlaying) DontDestroyOnLoad(gameObject);
                EnsureInitialized();
            }
            else if (_instance != this)
            {
                if (Application.isPlaying) Destroy(gameObject);
                else DestroyImmediate(gameObject);
            }
        }

        public void EnsureInitialized()
        {
            if (_db == null) LoadAccountsDatabase();
            if (_db == null) _db = new AccountsDatabase();
            if (_db.accounts == null) _db.accounts = new List<UserAccount>();
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

        public void Register(string username, string email, string password, string confirmPassword, Action<bool, string> callback)
        {
            EnsureInitialized();
            username = username?.Trim();
            email = email?.Trim();

            if (string.IsNullOrEmpty(username) || username.Length < 3)
            {
                callback?.Invoke(false, "Tên tài khoản phải có từ 3 ký tự trở lên!");
                return;
            }

            if (username.Length > 16)
            {
                callback?.Invoke(false, "Tên tài khoản tối đa 16 ký tự!");
                return;
            }

            if (!Regex.IsMatch(username, @"^[a-zA-Z0-9_ -]+$"))
            {
                callback?.Invoke(false, "Tên tài khoản chỉ được chứa chữ cái, số, gạch dưới!");
                return;
            }

            if (string.IsNullOrEmpty(password) || password.Length < 4)
            {
                callback?.Invoke(false, "Mật khẩu phải từ 4 ký tự trở lên!");
                return;
            }

            if (password != confirmPassword)
            {
                callback?.Invoke(false, "Mật khẩu nhập lại không khớp!");
                return;
            }

            var existing = _db.accounts.Find(a => a.username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                callback?.Invoke(false, "Tên tài khoản này đã có người sử dụng!");
                return;
            }

            var newAccount = new UserAccount
            {
                username = username,
                email = email,
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

            if (account.passwordHash != HashPassword(password))
            {
                callback?.Invoke(false, "Mật khẩu không chính xác!");
                return;
            }

            account.lastLoginDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            SaveAccountsDatabase();

            CurrentUser = account;
            SyncWithSaveManager(account);
            OnLoginSuccess?.Invoke(account);
            callback?.Invoke(true, "Đăng nhập thành công!");
        }

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
                foreach (byte b in bytes) builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }
    }
}
