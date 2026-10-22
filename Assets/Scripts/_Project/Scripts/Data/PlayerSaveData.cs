using System;
using System.Collections.Generic;
using UnityEngine;

namespace MangoHeist.Data
{
    [Serializable]
    public class PlayerSaveData
    {
        public int schemaVersion = 2;

        [Header("Hồ sơ người chơi")]
        public string playerId = "guest_player";
        public string playerName = "Tập Sự Hái Xoài";
        public int playerLevel = 1;
        public int playerExp = 0;
        public int mangoCoins = 100;

        [Header("Thống kê trận đấu")]
        public int matchesPlayed = 0;
        public int matchesWon = 0;
        public int totalMangoesDelivered = 0;

        [Header("Tiến trình")]
        public int highestStageCleared = 0;
        public List<string> unlockedMapIds = new List<string>();
        public List<StageRecord> stageRecords = new List<StageRecord>();

        [Header("Trang phục")]
        public List<string> ownedSkins = new List<string>();
        public string equippedHat = "non_la_mac_dinh";
        public string equippedBag = "tui_vai_mac_dinh";

        [Header("Cloud Save")]
        public long lastSavedTimestamp = 0;

        public static PlayerSaveData CreateDefault()
        {
            var data = new PlayerSaveData
            {
                playerId = "player_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                schemaVersion = 2,
                playerLevel = 1,
                playerExp = 0,
                mangoCoins = 100,
                highestStageCleared = 0,
                lastSavedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            data.unlockedMapIds.Add("map_01_vuon_nho");
            data.ownedSkins.Add("non_la_mac_dinh");
            data.ownedSkins.Add("tui_vai_mac_dinh");
            return data;
        }

        public static PlayerSaveData CreateForPlayer(string playerId, string playerName)
        {
            var data = CreateDefault();
            if (!string.IsNullOrWhiteSpace(playerId)) data.playerId = playerId;
            if (!string.IsNullOrWhiteSpace(playerName)) data.playerName = playerName;
            return data;
        }
    }

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
