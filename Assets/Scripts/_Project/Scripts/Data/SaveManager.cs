using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using UnityEngine;

namespace MangoHeist.Data
{
    /// <summary>
    /// Cloud Save adapter giữ API gameplay cũ. Chỉ tải/lưu sau khi UGS xác thực thành công.
    /// Lỗi tải cloud không được hiểu nhầm thành tài khoản mới và không tự ghi đè dữ liệu cũ.
    /// </summary>
    public class SaveManager : MonoBehaviour
    {
        private const string CloudSaveKey = "playerSaveData_v1";
        private static SaveManager _instance;
        private Task _saveQueue = Task.CompletedTask;

        public static SaveManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<SaveManager>();
                    if (_instance == null)
                    {
                        var go = new GameObject("[SaveManager]");
                        _instance = go.AddComponent<SaveManager>();
                    }
                }
                return _instance;
            }
        }

        [SerializeField] private PlayerSaveData _currentData;
        [SerializeField] private string _activePlayerId = "";

        public PlayerSaveData CurrentData => _currentData;
        public string ActivePlayerId => _activePlayerId;
        public string CurrentPlayerId => _activePlayerId;
        public bool IsDataLoaded { get; private set; }
        public string LastError { get; private set; }

        public event Action<PlayerSaveData> OnDataLoaded;
        public event Action<PlayerSaveData> OnDataSaved;
        public event Action<string> OnMapUnlocked;
        public event Action<int> OnCoinsChanged;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);
            // Không tải user "default" trong Awake: chưa xác thực tài khoản thì không được gọi Cloud Save.
        }

        // Tương thích API cũ, nhưng không tự tải dữ liệu trước khi đăng nhập.
        public void EnsureInitialized() { }

        public async Task<bool> SetCurrentPlayerAsync(string playerId, string playerName, Action<string> errorCallback = null)
        {
            _activePlayerId = (playerId ?? string.Empty).Trim();
            _currentData = null;
            IsDataLoaded = false;
            LastError = null;

            if (string.IsNullOrWhiteSpace(_activePlayerId))
            {
                LastError = "Player ID rỗng.";
                errorCallback?.Invoke(LastError);
                return false;
            }

            try
            {
                if (!AuthenticationService.Instance.IsSignedIn || AuthenticationService.Instance.PlayerId != _activePlayerId)
                    throw new InvalidOperationException("UGS chưa đăng nhập đúng Player ID.");

                var items = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { CloudSaveKey });
                PlayerSaveData loadedData = null;

                if (items != null && items.TryGetValue(CloudSaveKey, out var item))
                {
                    string json = item.Value.GetAs<string>();
                    if (string.IsNullOrWhiteSpace(json))
                        throw new InvalidDataException("Cloud Save có key hồ sơ nhưng dữ liệu rỗng.");

                    loadedData = JsonUtility.FromJson<PlayerSaveData>(json);
                    if (loadedData == null)
                        throw new InvalidDataException("Không thể giải mã PlayerSaveData từ Cloud Save.");
                }

                bool isNewSave = loadedData == null;
                _currentData = isNewSave
                    ? PlayerSaveData.CreateForPlayer(_activePlayerId, string.IsNullOrWhiteSpace(playerName) ? "Người Chơi Mới" : playerName.Trim())
                    : loadedData;

                _currentData.playerId = _activePlayerId;
                if (string.IsNullOrWhiteSpace(_currentData.playerName))
                    _currentData.playerName = string.IsNullOrWhiteSpace(playerName) ? "Người Chơi Mới" : playerName.Trim();

                bool changed = NormalizeData(_currentData);
                IsDataLoaded = true;

                // Chỉ tạo hồ sơ khi LoadAsync thành công và key thực sự chưa tồn tại.
                if (isNewSave || changed)
                    await SaveGameAsync();

                OnDataLoaded?.Invoke(_currentData);
                Debug.Log($"[SaveManager] Cloud Save đã tải cho {_currentData.playerName} ({_activePlayerId}). New save={isNewSave} | Matches={_currentData.matchesPlayed} | Wins={_currentData.matchesWon} | Delivered={_currentData.totalMangoesDelivered}");
                return true;
            }
            catch (Exception ex)
            {
                _currentData = null;
                IsDataLoaded = false;
                LastError = ex.Message;
                Debug.LogError($"[SaveManager] Không tải được Cloud Save; không tạo save thay thế: {ex}");
                errorCallback?.Invoke(LastError);
                return false;
            }
        }

        // Wrapper tương thích với code cũ. Luồng đăng nhập nên dùng SetCurrentPlayerAsync và await kết quả.
        public void SetCurrentPlayer(string playerId, string playerName)
            => _ = SetCurrentPlayerCompatAsync(playerId, playerName);

        private async Task SetCurrentPlayerCompatAsync(string playerId, string playerName)
        {
            bool ok = await SetCurrentPlayerAsync(playerId, playerName);
            if (!ok) Debug.LogError($"[SaveManager] Không thể chuyển người chơi: {LastError}");
        }

        public void ResetSession()
        {
            _activePlayerId = string.Empty;
            _currentData = null;
            IsDataLoaded = false;
            LastError = null;
            Debug.Log("[SaveManager] Đã xóa dữ liệu phiên khỏi RAM; dữ liệu cloud không bị xóa.");
        }

        public void NewGame(string playerId, string playerName)
        {
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                Debug.LogError("[SaveManager] Không thể tạo hồ sơ Cloud Save khi chưa đăng nhập.");
                return;
            }

            _activePlayerId = AuthenticationService.Instance.PlayerId;
            if (!string.IsNullOrWhiteSpace(playerId) && playerId != _activePlayerId)
                Debug.LogWarning("[SaveManager] playerId được yêu cầu khác Player ID đã xác thực; dùng ID từ Authentication.");

            _currentData = PlayerSaveData.CreateForPlayer(_activePlayerId,
                string.IsNullOrWhiteSpace(playerName) ? "Người Chơi Mới" : playerName.Trim());
            IsDataLoaded = true;
            SaveGame();
        }

        [ContextMenu("Lưu dữ liệu lên Cloud Save")]
        public void SaveGame() => _ = SaveGameCompatAsync();

        public Task SaveGameAsync()
        {
            if (!IsDataLoaded || _currentData == null)
                throw new InvalidOperationException("Chưa tải hồ sơ cloud thành công; từ chối ghi save.");
            if (!AuthenticationService.Instance.IsSignedIn || AuthenticationService.Instance.PlayerId != _activePlayerId)
                throw new InvalidOperationException("Không ghi save vì phiên UGS không khớp với người chơi đang hoạt động.");

            _currentData.lastSavedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string snapshotJson = JsonUtility.ToJson(_currentData, true);
            string playerIdSnapshot = _activePlayerId;

            Task previousSave = _saveQueue;
            _saveQueue = PersistSnapshotAfterAsync(previousSave, playerIdSnapshot, snapshotJson);
            return _saveQueue;
        }

        private async Task PersistSnapshotAfterAsync(Task previousSave, string playerIdSnapshot, string snapshotJson)
        {
            // Một lần save lỗi không chặn vĩnh viễn các lần save sau; mỗi lần đều kiểm tra lại phiên.
            try { if (previousSave != null) await previousSave; } catch { }

            if (!AuthenticationService.Instance.IsSignedIn ||
                AuthenticationService.Instance.PlayerId != playerIdSnapshot ||
                _activePlayerId != playerIdSnapshot)
                throw new InvalidOperationException("Hủy save vì tài khoản đã thay đổi trong lúc chờ ghi.");

            var payload = new Dictionary<string, object> { { CloudSaveKey, snapshotJson } };
            await CloudSaveService.Instance.Data.Player.SaveAsync(payload);
            if (IsDataLoaded && _activePlayerId == playerIdSnapshot)
                OnDataSaved?.Invoke(_currentData);
            Debug.Log($"[SaveManager] Đã lưu Cloud Save cho {playerIdSnapshot}.");
        }

        private async Task SaveGameCompatAsync()
        {
            try { await SaveGameAsync(); }
            catch (Exception ex) { LastError = ex.Message; Debug.LogError($"[SaveManager] Lưu Cloud Save thất bại: {ex.Message}"); }
        }

        [ContextMenu("Tải lại dữ liệu từ Cloud Save")]
        public void LoadGame() => _ = LoadGameCompatAsync();

        private async Task LoadGameCompatAsync()
        {
            string name = _currentData != null ? _currentData.playerName : "Người Chơi Mới";
            bool ok = await SetCurrentPlayerAsync(_activePlayerId, name);
            if (!ok) Debug.LogError($"[SaveManager] LoadGame thất bại: {LastError}");
        }

        private static bool NormalizeData(PlayerSaveData data)
        {
            bool changed = false;
            if (data.unlockedMapIds == null) { data.unlockedMapIds = new List<string>(); changed = true; }
            if (data.stageRecords == null) { data.stageRecords = new List<StageRecord>(); changed = true; }
            if (data.ownedSkins == null) { data.ownedSkins = new List<string>(); changed = true; }
            if (!data.unlockedMapIds.Contains("map_01_vuon_nho")) { data.unlockedMapIds.Insert(0, "map_01_vuon_nho"); changed = true; }
            if (!data.ownedSkins.Contains("non_la_mac_dinh")) { data.ownedSkins.Add("non_la_mac_dinh"); changed = true; }
            if (!data.ownedSkins.Contains("tui_vai_mac_dinh")) { data.ownedSkins.Add("tui_vai_mac_dinh"); changed = true; }
            if (data.playerLevel < 1) { data.playerLevel = 1; changed = true; }
            if (data.playerExp < 0) { data.playerExp = 0; changed = true; }
            if (data.mangoCoins < 0) { data.mangoCoins = 0; changed = true; }

            // Migration v2: persistent match statistics.
            if (data.matchesPlayed < 0) { data.matchesPlayed = 0; changed = true; }
            if (data.matchesWon < 0) { data.matchesWon = 0; changed = true; }
            if (data.totalMangoesDelivered < 0) { data.totalMangoesDelivered = 0; changed = true; }
            if (data.matchesWon > data.matchesPlayed)
            {
                data.matchesPlayed = data.matchesWon;
                changed = true;
            }

            if (data.schemaVersion < 2) { data.schemaVersion = 2; changed = true; }
            return changed;
        }

        public bool UnlockMap(string mapId)
        {
            if (string.IsNullOrWhiteSpace(mapId)) return false;
            if (!IsDataLoaded || _currentData == null)
            {
                Debug.LogWarning("[SaveManager] Chưa tải hồ sơ cloud; bỏ qua UnlockMap.");
                return false;
            }

            mapId = mapId.Trim();
            if (_currentData.unlockedMapIds.Contains(mapId)) return false;
            _currentData.unlockedMapIds.Add(mapId);
            SaveGame();
            OnMapUnlocked?.Invoke(mapId);
            return true;
        }

        public bool IsMapUnlocked(string mapId)
        {
            return !string.IsNullOrWhiteSpace(mapId) && IsDataLoaded && _currentData != null &&
                   _currentData.unlockedMapIds != null && _currentData.unlockedMapIds.Contains(mapId.Trim());
        }

        /// <summary>
        /// Cập nhật thống kê sau khi trận thắng được xác nhận trong gameplay.
        /// Hàm này chỉ cập nhật snapshot trong RAM; bên gọi phải gọi và await SaveGameAsync().
        /// Không dùng cho phần thưởng/tiền tệ cần chống gian lận.
        /// </summary>
        public void RecordMatchWinStats(int mangoesDeliveredThisMatch)
        {
            if (!IsDataLoaded || _currentData == null)
                throw new InvalidOperationException("Chưa tải hồ sơ cloud; không thể ghi thống kê trận.");

            if (!AuthenticationService.Instance.IsSignedIn ||
                AuthenticationService.Instance.PlayerId != _activePlayerId)
                throw new InvalidOperationException("Phiên UGS không khớp với hồ sơ đang hoạt động.");

            _currentData.matchesPlayed = SaturatingAddNonNegative(_currentData.matchesPlayed, 1);
            _currentData.matchesWon = SaturatingAddNonNegative(_currentData.matchesWon, 1);
            _currentData.totalMangoesDelivered = SaturatingAddNonNegative(
                _currentData.totalMangoesDelivered,
                Mathf.Max(0, mangoesDeliveredThisMatch));

            Debug.Log(
                $"[SaveManager] Đã cập nhật thống kê trận trong RAM: " +
                $"Matches={_currentData.matchesPlayed}, Wins={_currentData.matchesWon}, " +
                $"TotalDelivered={_currentData.totalMangoesDelivered}. Cần lưu Cloud Save.",
                this);
        }

        private static int SaturatingAddNonNegative(int current, int increment)
        {
            if (current < 0) current = 0;
            if (increment < 0) increment = 0;
            long total = (long)current + increment;
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        // Dành cho MVP. Với game online phát hành thật, phần thưởng/xu phải được xác minh phía server
        // (Cloud Code/server-authoritative), vì client có thể gọi API và sửa dữ liệu của chính mình.
        public void AddMangoCoins(int amount)
        {
            if (amount <= 0 || !IsDataLoaded || _currentData == null) return;
            long total = (long)_currentData.mangoCoins + amount;
            _currentData.mangoCoins = total > int.MaxValue ? int.MaxValue : (int)total;
            SaveGame();
            OnCoinsChanged?.Invoke(_currentData.mangoCoins);
        }

        public bool SpendMangoCoins(int amount)
        {
            if (amount <= 0 || !IsDataLoaded || _currentData == null) return false;
            if (_currentData.mangoCoins < amount) return false;
            _currentData.mangoCoins -= amount;
            SaveGame();
            OnCoinsChanged?.Invoke(_currentData.mangoCoins);
            return true;
        }

        [ContextMenu("Mở hướng dẫn Cloud Save")]
        public void OpenSaveFolder()
        {
            Debug.Log("Dữ liệu hiện lưu trên Unity Cloud Save. Xem tại Unity Dashboard → Cloud Save/Player Data; không có thư mục save JSON local.");
        }
    }
}
