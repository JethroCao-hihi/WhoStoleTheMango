using System;
using System.IO;
using UnityEngine;

namespace MangoHeist.Data
{
    /// <summary>
    /// Quản lý việc Đọc và Ghi dữ liệu người chơi vào file JSON trên máy (Local Save).
    /// Hoạt động độc lập, không cần kết nối mạng.
    /// Peter hoặc bất kỳ ai trong team chỉ cần gọi:
    ///   SaveManager.Instance.UnlockMap("map_02_vuon_cat_chu");
    ///   SaveManager.Instance.IsMapUnlocked("map_02_vuon_cat_chu");
    /// </summary>
    public class SaveManager : MonoBehaviour
    {
        private static SaveManager _instance;
        public static SaveManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var existing = FindAnyObjectByType<SaveManager>();
                    if (existing != null)
                    {
                        _instance = existing;
                    }
                    else
                    {
                        var go = new GameObject("[SaveManager]");
                        _instance = go.AddComponent<SaveManager>();
                        if (Application.isPlaying)
                        {
                            DontDestroyOnLoad(go);
                        }
                    }
                }
                return _instance;
            }
        }

        [SerializeField] private PlayerSaveData _currentData;
        public PlayerSaveData CurrentData => _currentData;

        // Đường dẫn file: C:\Users\<User>\AppData\LocalLow\<Company>\<Product>\player_save.json
        private string SaveFilePath => Path.Combine(Application.persistentDataPath, "player_save.json");

        // Các sự kiện thông báo để UI tự động cập nhật
        public event Action<PlayerSaveData> OnDataLoaded;
        public event Action<PlayerSaveData> OnDataSaved;
        public event Action<string> OnMapUnlocked;

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                if (Application.isPlaying)
                {
                    DontDestroyOnLoad(gameObject);
                }
                LoadGame();
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

        /// <summary>
        /// Lưu toàn bộ dữ liệu hiện tại xuống file JSON trên máy
        /// </summary>
        [ContextMenu("Lưu dữ liệu (Save Game)")]
        public void SaveGame()
        {
            try
            {
                if (_currentData == null)
                {
                    _currentData = PlayerSaveData.CreateDefault();
                }

                _currentData.lastSavedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                string json = JsonUtility.ToJson(_currentData, true);
                File.WriteAllText(SaveFilePath, json);

                Debug.Log($"<color=#4CAF50><b>[SaveManager]</b> Đã lưu thành công tại:</color> {SaveFilePath}");
                OnDataSaved?.Invoke(_currentData);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveManager] Lỗi khi lưu file: {ex.Message}");
            }
        }

        /// <summary>
        /// Đọc dữ liệu từ file JSON trên máy. Nếu chưa có file thì tạo mới.
        /// </summary>
        [ContextMenu("Tải dữ liệu (Load Game)")]
        public void LoadGame()
        {
            try
            {
                if (File.Exists(SaveFilePath))
                {
                    string json = File.ReadAllText(SaveFilePath);
                    _currentData = JsonUtility.FromJson<PlayerSaveData>(json);
                    Debug.Log("<color=#2196F3><b>[SaveManager]</b> Đã tải dữ liệu người chơi thành công!</color>");
                }
                else
                {
                    Debug.Log("<color=#FF9800><b>[SaveManager]</b> Chưa có file save cũ, khởi tạo dữ liệu mặc định...</color>");
                    _currentData = PlayerSaveData.CreateDefault();
                    SaveGame();
                }

                OnDataLoaded?.Invoke(_currentData);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveManager] Lỗi khi đọc file save: {ex.Message}. Khởi tạo lại.");
                _currentData = PlayerSaveData.CreateDefault();
            }
        }

        /// <summary>
        /// Kiểm tra một map cụ thể đã được mở khóa hay chưa
        /// </summary>
        public bool IsMapUnlocked(string mapId)
        {
            if (_currentData == null) LoadGame();
            return _currentData.unlockedMapIds.Contains(mapId);
        }

        /// <summary>
        /// Mở khóa map mới khi người chơi qua màn Offline
        /// </summary>
        public bool UnlockMap(string mapId)
        {
            if (_currentData == null) LoadGame();

            if (!_currentData.unlockedMapIds.Contains(mapId))
            {
                _currentData.unlockedMapIds.Add(mapId);
                SaveGame();
                Debug.Log($"<color=#E91E63>🎉 <b>[MỞ KHÓA MAP THÀNH CÔNG]</b>: {mapId}</color>");
                OnMapUnlocked?.Invoke(mapId);
                return true;
            }

            Debug.Log($"[SaveManager] Map '{mapId}' đã được mở từ trước.");
            return false;
        }

        /// <summary>
        /// Thưởng xu xoài (sau khi thắng trận online hoặc hoàn thành ải offline)
        /// </summary>
        public void AddMangoCoins(int amount)
        {
            if (_currentData == null) LoadGame();
            _currentData.mangoCoins += amount;
            SaveGame();
            Debug.Log($"[SaveManager] Đã cộng {amount} xu xoài. Hiện có: {_currentData.mangoCoins}");
        }

        /// <summary>
        /// Cập nhật màn chơi Offline cao nhất đã vượt qua
        /// </summary>
        public void SetHighestStageCleared(int stageNumber)
        {
            if (_currentData == null) LoadGame();
            if (stageNumber > _currentData.highestStageCleared)
            {
                _currentData.highestStageCleared = stageNumber;
                SaveGame();
            }
        }

        /// <summary>
        /// Mở thư mục chứa file save trong Windows Explorer
        /// </summary>
        [ContextMenu("Mở thư mục chứa file Save")]
        public void OpenSaveFolder()
        {
            string folder = Application.persistentDataPath.Replace('/', '\\');
            System.Diagnostics.Process.Start("explorer.exe", folder);
        }

        /// <summary>
        /// Xóa file save cũ và đặt lại dữ liệu mặc định (Dùng để test)
        /// </summary>
        [ContextMenu("Reset dữ liệu Save về mặc định")]
        public void ResetSaveData()
        {
            if (File.Exists(SaveFilePath))
            {
                File.Delete(SaveFilePath);
            }
            _currentData = PlayerSaveData.CreateDefault();
            SaveGame();
            Debug.LogWarning("[SaveManager] Đã reset toàn bộ dữ liệu người chơi về mặc định.");
        }
    }
}
