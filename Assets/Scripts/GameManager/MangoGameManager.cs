using System;
using System.Threading.Tasks;
using Fusion;
using MangoHeist.Data;
using UnityEngine;

namespace Assets.Scripts.GameManager
{
    /// <summary>
    /// Detects the replicated thief-team delivery goal on each client.
    /// Each signed-in client writes statistics only to that client's own Cloud Save profile.
    /// This is an MVP stats path, not an anti-cheat reward authority.
    /// </summary>
    [DisallowMultipleComponent]
    public class MangoGameManager : MonoBehaviour
    {
        [Header("Match result")]
        [SerializeField, Min(0.1f)] private float scanInterval = 0.25f;
        [SerializeField] private bool saveStatsToCloud = true;

        private static MangoGameManager _instance;
        private float _nextScanTime;
        private float _nextSaveAttemptTime;
        private bool _winDetected;
        private bool _statsApplied;
        private bool _saveInProgress;
        private bool _savedToCloud;
        private bool _warnedNoCloudProfile;
        private bool _warnedNoLocalPlayer;

        public bool HasTeamWon => _winDetected;
        public bool HasSavedResult => _savedToCloud;
        public event Action OnTeamVictory;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[MangoGameManager] Có hơn một MangoGameManager trong scene; bỏ component trùng.", this);
                Destroy(this);
                return;
            }

            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextScanTime)
                return;

            _nextScanTime = Time.unscaledTime + Mathf.Max(0.1f, scanInterval);

            int teamTotal = GetTeamDeliveredTotal();
            if (teamTotal < NetworkPlayerData.TeamMangoGoal)
                return;

            if (!_winDetected)
            {
                _winDetected = true;
                Debug.Log(
                    $"[GAMEPLAY] Đội trộm thắng: đã giao {teamTotal}/{NetworkPlayerData.TeamMangoGoal} quả xoài.",
                    this);
                OnTeamVictory?.Invoke();
            }

            if (!saveStatsToCloud || _savedToCloud || _saveInProgress ||
                Time.unscaledTime < _nextSaveAttemptTime)
                return;

            _ = SaveWinStatsAsync();
        }

        private async Task SaveWinStatsAsync()
        {
            _saveInProgress = true;

            try
            {
                SaveManager saveManager = SaveManager.Instance;
                if (saveManager == null || !saveManager.IsDataLoaded || saveManager.CurrentData == null)
                {
                    if (!_warnedNoCloudProfile)
                    {
                        Debug.LogWarning(
                            "[MangoGameManager] Đội đã thắng nhưng hồ sơ Cloud Save chưa tải. " +
                            "Đăng nhập và tải hồ sơ trước khi vào trận; sẽ thử lại.",
                            this);
                        _warnedNoCloudProfile = true;
                    }

                    _nextSaveAttemptTime = Time.unscaledTime + 3f;
                    return;
                }

                if (!TryGetLocalPlayerDeliveredCount(out int personalDelivered))
                {
                    if (!_warnedNoLocalPlayer)
                    {
                        Debug.LogWarning(
                            "[MangoGameManager] Chưa tìm thấy NetworkPlayerData có Input Authority; sẽ thử lại.",
                            this);
                        _warnedNoLocalPlayer = true;
                    }

                    _nextSaveAttemptTime = Time.unscaledTime + 3f;
                    return;
                }

                _warnedNoCloudProfile = false;
                _warnedNoLocalPlayer = false;

                // Only mutate the in-memory stats once. If the network save fails,
                // retries save the same snapshot instead of incrementing stats repeatedly.
                if (!_statsApplied)
                {
                    saveManager.RecordMatchWinStats(personalDelivered);
                    _statsApplied = true;
                }

                await saveManager.SaveGameAsync();
                _savedToCloud = true;

                PlayerSaveData data = saveManager.CurrentData;
                Debug.Log(
                    $"[MangoGameManager] Đã lưu thống kê trận thắng lên Cloud Save. " +
                    $"Player={saveManager.ActivePlayerId}, PersonalDelivered={personalDelivered}, " +
                    $"Matches={data.matchesPlayed}, Wins={data.matchesWon}, " +
                    $"TotalDelivered={data.totalMangoesDelivered}.",
                    this);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MangoGameManager] Lưu thống kê Cloud Save thất bại; sẽ thử lại: {ex.Message}", this);
                _nextSaveAttemptTime = Time.unscaledTime + 5f;
            }
            finally
            {
                _saveInProgress = false;
            }
        }

        private static int GetTeamDeliveredTotal()
        {
            NetworkPlayerData[] players =
                FindObjectsByType<NetworkPlayerData>(FindObjectsSortMode.None);

            int total = 0;
            foreach (NetworkPlayerData player in players)
            {
                if (player == null || player.Object == null || !player.Object.IsValid)
                    continue;

                total += Mathf.Max(0, player.DepositedMangoCount);
            }

            return total;
        }

        private static bool TryGetLocalPlayerDeliveredCount(out int delivered)
        {
            NetworkPlayerData[] players =
                FindObjectsByType<NetworkPlayerData>(FindObjectsSortMode.None);

            foreach (NetworkPlayerData player in players)
            {
                if (player == null || player.Object == null || !player.Object.IsValid)
                    continue;

                if (player.Object.HasInputAuthority)
                {
                    delivered = Mathf.Max(0, player.DepositedMangoCount);
                    return true;
                }
            }

            delivered = 0;
            return false;
        }
    }
}
