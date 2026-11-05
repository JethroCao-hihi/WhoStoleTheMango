
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHarvestHUD : MonoBehaviour
{
    [Header("HUD References")]
    [SerializeField] private TMP_Text mangoCountText;
    [SerializeField] private GameObject harvestPanel;
    [SerializeField] private TMP_Text harvestLabel;
    [SerializeField] private Slider harvestSlider;
    [SerializeField] private TMP_Text interactionHintText;

    [Header("Interaction")]
    [SerializeField] private float climbPromptRadius = 2f;
    [SerializeField] private float harvestPromptRadius = 3f;
    [SerializeField] private float hintRefreshInterval = 0.15f;

    [Header("Team Progress")]
    [SerializeField] private TMP_Text teamMangoProgressText;

    private float nextTeamProgressRefresh;
    private float nextHintRefreshTime;

    private NetworkPlayerController localController;
    private NetworkPlayerData localPlayerData;

    private string currentHint = "";

    private void Awake()
    {
        // Keep the scene-authored HUD visible when Fusion loads GameScene. Only
        // gameplay-only prompts and the harvesting panel are hidden by Update.
        Canvas hudCanvas = GetComponent<Canvas>();
        if (hudCanvas != null)
        {
            hudCanvas.gameObject.SetActive(true);
            hudCanvas.enabled = true;

            for (Transform current = hudCanvas.transform; current != null; current = current.parent)
            {
                Vector3 scale = current.localScale;
                if (Mathf.Abs(scale.x) < 0.0001f ||
                    Mathf.Abs(scale.y) < 0.0001f ||
                    Mathf.Abs(scale.z) < 0.0001f)
                {
                    current.localScale = Vector3.one;
                }
            }
        }

        if (mangoCountText != null)
            mangoCountText.gameObject.SetActive(true);

        if (teamMangoProgressText != null)
            teamMangoProgressText.gameObject.SetActive(true);

        ShowWaitingHUD();
    }

    private void Update()
    {
        // Kiểm tra nhân vật hiện tại còn tồn tại và đã được Fusion spawn.
        if (!IsLocalPlayerReady())
        {
            localController = null;
            localPlayerData = null;

            FindLocalPlayer();
        }

        // Chưa có local player hợp lệ: không đọc Networked Properties.
        if (!IsLocalPlayerReady())
        {
            ShowWaitingHUD();

            RefreshTeamProgressIfNeeded();
            return;
        }

        // Hiển thị số xoài trong túi.
        if (mangoCountText != null)
        {
            mangoCountText.text =
                $"Xoài: {localPlayerData.MangoCount} / " +
                $"{NetworkPlayerData.MaxMangoCount}";
        }

        // Hiển thị tiến trình hái.
        bool isHarvesting = localController.IsHarvesting;

        if (harvestPanel != null)
        {
            harvestPanel.SetActive(isHarvesting);
        }

        if (isHarvesting)
        {
            float progress = Mathf.Clamp01(
                localController.HarvestProgress
            );

            if (harvestSlider != null)
                harvestSlider.value = progress;

            if (harvestLabel != null)
            {
                harvestLabel.text =
                    $"Đang hái xoài... {progress * 100f:0}%";
            }
        }

        // Không quét toàn bộ cây và xoài mỗi frame.
        if (Time.unscaledTime >= nextHintRefreshTime)
        {
            nextHintRefreshTime =
                Time.unscaledTime + hintRefreshInterval;

            UpdateInteractionHint();
        }

        RefreshTeamProgressIfNeeded();
    }

    /// <summary>
    /// Networked Properties chỉ được đọc khi NetworkObject đã hợp lệ.
    /// Controller và PlayerData phải thuộc cùng một NetworkObject.
    /// </summary>
    private bool IsLocalPlayerReady()
    {
        if (localController == null || localPlayerData == null)
            return false;

        NetworkObject controllerObject =
            localController.GetComponent<NetworkObject>();

        NetworkObject dataObject =
            localPlayerData.GetComponent<NetworkObject>();

        return controllerObject != null &&
               controllerObject.IsValid &&
               controllerObject.HasInputAuthority &&
               dataObject != null &&
               dataObject.IsValid &&
               dataObject == controllerObject;
    }

    private void ShowWaitingHUD()
    {
        if (mangoCountText != null)
            mangoCountText.text = "Xoài: -- / 5";

        if (harvestPanel != null)
            harvestPanel.SetActive(false);

        if (harvestSlider != null)
            harvestSlider.value = 0f;

        SetInteractionHint("");
    }

    private void RefreshTeamProgressIfNeeded()
    {
        if (Time.unscaledTime < nextTeamProgressRefresh)
            return;

        nextTeamProgressRefresh =
            Time.unscaledTime + 0.25f;

        UpdateTeamMangoProgress();
    }

    private void UpdateInteractionHint()
    {
        // Hàm này chỉ được gọi sau khi IsLocalPlayerReady() thành công.
        if (!IsLocalPlayerReady())
            return;

        if (localController.IsClimbingForHUD)
        {
            SetInteractionHint("Đang leo cây...");
            return;
        }

        if (localController.IsHarvestingForHUD)
        {
            SetInteractionHint("Đang hái xoài...");
            return;
        }

        if (localController.IsOnTreeForHUD)
        {
            if (localPlayerData.MangoCount >=
                NetworkPlayerData.MaxMangoCount)
            {
                SetInteractionHint("Túi đầy — E để leo xuống");
                return;
            }

            NetworkMango mango = FindNearestMango();

            if (mango != null)
                SetInteractionHint("F — Hái xoài");
            else
                SetInteractionHint("E — Leo xuống");

            return;
        }

        MangoTree tree = FindNearestTree();

        if (tree != null)
            SetInteractionHint("E — Leo cây");
        else
            SetInteractionHint("");
    }

    private MangoTree FindNearestTree()
    {
        if (!IsLocalPlayerReady())
            return null;

        MangoTree[] trees =
            FindObjectsByType<MangoTree>(
                FindObjectsSortMode.None
            );

        MangoTree nearest = null;

        float bestDistanceSquared =
            climbPromptRadius * climbPromptRadius;

        foreach (MangoTree tree in trees)
        {
            if (tree == null || !tree.HasClimbPoints)
                continue;

            Vector3 delta =
                localController.transform.position -
                tree.ClimbStartPoint.position;

            delta.y = 0f;

            float distanceSquared = delta.sqrMagnitude;

            if (distanceSquared <= bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                nearest = tree;
            }
        }

        return nearest;
    }

    private NetworkMango FindNearestMango()
    {
        if (!IsLocalPlayerReady())
            return null;

        NetworkMango[] mangoes =
            FindObjectsByType<NetworkMango>(
                FindObjectsSortMode.None
            );

        NetworkMango nearest = null;

        float bestDistanceSquared =
            harvestPromptRadius * harvestPromptRadius;

        foreach (NetworkMango mango in mangoes)
        {
            if (mango == null)
                continue;

            // Không đọc trạng thái network của xoài chưa spawn.
            NetworkObject mangoObject =
                mango.GetComponent<NetworkObject>();

            if (mangoObject == null || !mangoObject.IsValid)
                continue;

            if (!mango.CanBeCollected)
                continue;

            float distanceSquared =
                (mango.transform.position -
                 localController.transform.position).sqrMagnitude;

            if (distanceSquared <= bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                nearest = mango;
            }
        }

        return nearest;
    }

    private void FindLocalPlayer()
    {
        NetworkPlayerController[] players =
            FindObjectsByType<NetworkPlayerController>(
                FindObjectsSortMode.None
            );

        foreach (NetworkPlayerController player in players)
        {
            if (player == null)
                continue;

            NetworkObject networkObject =
                player.GetComponent<NetworkObject>();

            // Chờ Fusion hoàn tất việc spawn.
            if (networkObject == null ||
                !networkObject.IsValid ||
                !networkObject.HasInputAuthority)
            {
                continue;
            }

            NetworkPlayerData data =
                player.GetComponent<NetworkPlayerData>();

            if (data == null)
                continue;

            NetworkObject dataObject =
                data.GetComponent<NetworkObject>();

            if (dataObject == null ||
                !dataObject.IsValid ||
                dataObject != networkObject)
            {
                continue;
            }

            localController = player;
            localPlayerData = data;

            Debug.Log(
                "[HUD] Đã tìm thấy local player đã spawn.",
                player
            );

            return;
        }
    }

    private void SetInteractionHint(string message)
    {
        if (interactionHintText == null ||
            currentHint == message)
        {
            return;
        }

        currentHint = message;

        interactionHintText.text = message;

        interactionHintText.gameObject.SetActive(
            !string.IsNullOrEmpty(message)
        );
    }

    private void UpdateTeamMangoProgress()
    {
        if (teamMangoProgressText == null)
            return;

        NetworkPlayerData[] players =
            FindObjectsByType<NetworkPlayerData>(
                FindObjectsSortMode.None
            );

        int totalDeposited = 0;

        foreach (NetworkPlayerData player in players)
        {
            if (player == null)
                continue;

            // Bỏ qua player prefab hoặc NetworkObject chưa spawn.
            NetworkObject networkObject =
                player.GetComponent<NetworkObject>();

            if (networkObject == null || !networkObject.IsValid)
                continue;

            totalDeposited += player.DepositedMangoCount;
        }

        teamMangoProgressText.text =
            $"Đội đã giao: {totalDeposited} / " +
            $"{NetworkPlayerData.TeamMangoGoal}";
    }
}
