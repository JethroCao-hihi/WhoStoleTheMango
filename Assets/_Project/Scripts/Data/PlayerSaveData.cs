using System;
using System.Collections.Generic;
using UnityEngine;

namespace MangoHeist.Data
{
    /// <summary>
    /// Chứa toàn bộ thông tin người chơi cần lưu trữ:
    /// - Tiến trình chơi Offline (Map đã mở, màn đã qua)
    /// - Tài sản (Xu xoài, Cấp độ, Kinh nghiệm)
    /// - Trang bị (Skin nón, skin túi)
    /// </summary>
    [Serializable]
    public class PlayerSaveData
    {
        [Header("Hồ sơ người chơi")]
        public string playerId = "guest_player";
        public string playerName = "Tập Sự Hái Xoài";
        public int playerLevel = 1;
        public int playerExp = 0;
        public int mangoCoins = 100; // Tặng sẵn 100 xu cho người chơi mới

        [Header("Tiến trình chơi Offline (Mở Map)")]
        public int highestStageCleared = 0; // Màn chơi offline cao nhất đã vượt qua
        public List<string> unlockedMapIds = new List<string>(); // Danh sách ID các map đã mở khóa
        public List<StageRecord> stageRecords = new List<StageRecord>(); // Kỷ lục điểm từng màn

        [Header("Trang phục & Túi đựng xoài")]
        public List<string> ownedSkins = new List<string>(); // Các skin đang sở hữu
        public string equippedHat = "non_la_mac_dinh";
        public string equippedBag = "tui_vai_mac_dinh";

        [Header("Đồng bộ Mây (Cloud Sync)")]
        public long lastSavedTimestamp = 0; // Dùng để đối chiếu khi đồng bộ lên Database

        /// <summary>
        /// Tạo dữ liệu ban đầu cho một người chơi mới tinh
        /// </summary>
        public static PlayerSaveData CreateDefault()
        {
            var data = new PlayerSaveData();
            data.playerId = "player_" + Guid.NewGuid().ToString().Substring(0, 8);
            
            // Mặc định mở sẵn Map đầu tiên
            data.unlockedMapIds.Add("map_01_vuon_nho");
            
            // Trang bị mặc định
            data.ownedSkins.Add("non_la_mac_dinh");
            data.ownedSkins.Add("tui_vai_mac_dinh");

            data.lastSavedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return data;
        }
    }

    /// <summary>
    /// Lưu số sao và điểm kỷ lục của từng màn chơi Offline
    /// </summary>
    [Serializable]
    public class StageRecord
    {
        public string stageId;
        public int starsEarned;
        public int highScore;

        public StageRecord() { }

        public StageRecord(string stageId, int starsEarned, int highScore)
        {
            this.stageId = stageId;
            this.starsEarned = starsEarned;
            this.highScore = highScore;
        }
    }
}
