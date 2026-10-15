
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

    private NetworkPlayerController localController;
    private NetworkPlayerData localPlayerData;

    private float nextHintRefreshTime;
    private string currentHint = "";

    private void Update()
    {
        if (localController == null || localPlayerData == null)
            FindLocalPlayer();

        if (localController == null || localPlayerData == null)
        {
            if (mangoCountText != null)
                mangoCountText.text = "Xoài: -- / 5";

            if (harvestPanel != null)
                harvestPanel.SetActive(false);

            SetInteractionHint("");
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

        if (harvestPanel != null &&
            harvestPanel.activeSelf != isHarvesting)
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

        if (Time.unscaledTime >= nextTeamProgressRefresh)
        {
            nextTeamProgressRefresh = Time.unscaledTime + 0.25f;
            UpdateTeamMangoProgress();
        }
    }

    private void UpdateInteractionHint()
    {
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
            NetworkPlayerData data = localPlayerData;

            if (data.MangoCount >= NetworkPlayerData.MaxMangoCount)
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
        NetworkMango[] mangoes =
            FindObjectsByType<NetworkMango>(
                FindObjectsSortMode.None
            );

        NetworkMango nearest = null;
        float bestDistanceSquared =
            harvestPromptRadius * harvestPromptRadius;

        foreach (NetworkMango mango in mangoes)
        {
            if (mango == null || !mango.CanBeCollected)
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

            if (networkObject == null ||
                !networkObject.HasInputAuthority)
            {
                continue;
            }

            NetworkPlayerData data =
                player.GetComponent<NetworkPlayerData>();

            if (data == null)
                continue;

            localController = player;
            localPlayerData = data;

            Debug.Log("[HUD] Đã tìm thấy local player.", player);
            return;
        }
    }

    private void SetInteractionHint(string message)
    {
        if (interactionHintText == null ||
            currentHint == message)
            return;

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

            totalDeposited += player.DepositedMangoCount;
        }

        teamMangoProgressText.text =
            $"Đội đã giao: {totalDeposited} / " +
            $"{NetworkPlayerData.TeamMangoGoal}";
    }
}
