using Fusion;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Updates a world-space stamina bar authored as a child of the player prefab.
/// This component never creates UI objects.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkPlayerController))]
public class PlayerStaminaBar : NetworkBehaviour
{
    [Header("World-space position and size")]
    [SerializeField, Min(0.5f)] private float heightAbovePlayer = 2.25f;
    [SerializeField] private bool showOnlyForLocalPlayer = true;

    [Header("Prefab-authored UI")]
    [SerializeField] private Canvas worldCanvas;
    [SerializeField] private RectTransform canvasRect;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Image fillImage;

    [Header("Color thresholds")]
    [SerializeField, Range(0f, 1f)] private float warningThreshold = 0.5f;
    [SerializeField, Range(0f, 1f)] private float criticalThreshold = 0.25f;
    [SerializeField] private Color highStaminaColor = new Color(0.20f, 0.88f, 0.28f, 1f);
    [SerializeField] private Color mediumStaminaColor = new Color(1f, 0.78f, 0.12f, 1f);
    [SerializeField] private Color lowStaminaColor = new Color(1f, 0.20f, 0.16f, 1f);
    [SerializeField] private Color normalBackgroundColor = new Color(0.06f, 0.07f, 0.08f, 0.88f);
    [SerializeField] private Color emptyBackgroundColor = new Color(0.32f, 0.035f, 0.025f, 0.95f);

    [Header("Smoothing")]
    [SerializeField, Min(0f)] private float fillSmoothing = 8f;

    private NetworkPlayerController playerController;
    private Camera mainCamera;
    private bool shouldDisplay;
    private bool firstRefresh = true;

    public override void Spawned()
    {
        playerController = GetComponent<NetworkPlayerController>();
        mainCamera = Camera.main;

        shouldDisplay = !showOnlyForLocalPlayer ||
                        (Object != null && Object.HasInputAuthority);

        BindPrefabAuthoredBar();

        if (worldCanvas != null)
            worldCanvas.enabled = shouldDisplay;

        RefreshBar(true);
    }

    private void LateUpdate()
    {
        if (!shouldDisplay || worldCanvas == null || playerController == null)
            return;

        if (mainCamera == null)
            mainCamera = Camera.main;

        // Keep the bar at a fixed height over the moving/climbing player.
        canvasRect.position = transform.position + Vector3.up * heightAbovePlayer;

        // Billboard toward the active camera so it stays readable while orbiting.
        if (mainCamera != null)
        {
            Vector3 toCamera = mainCamera.transform.position - canvasRect.position;
            if (toCamera.sqrMagnitude > 0.0001f)
                canvasRect.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
        }

        RefreshBar(false);
    }

    private void BindPrefabAuthoredBar()
    {
        if (worldCanvas == null)
            worldCanvas = GetComponentInChildren<Canvas>(true);

        if (worldCanvas == null)
        {
            shouldDisplay = false;
            Debug.LogWarning(
                "PlayerStaminaBar has no prefab-authored World Space Canvas. Add one under the player prefab and assign it in the Inspector.",
                this);
            return;
        }

        if (canvasRect == null)
            canvasRect = worldCanvas.transform as RectTransform;

        if (backgroundImage == null || fillImage == null)
        {
            Image[] images = worldCanvas.GetComponentsInChildren<Image>(true);
            foreach (Image candidate in images)
            {
                if (backgroundImage == null && candidate.gameObject.name == "Background")
                    backgroundImage = candidate;
                else if (fillImage == null && candidate.gameObject.name == "Fill")
                    fillImage = candidate;
            }
        }

        if (canvasRect == null || backgroundImage == null || fillImage == null)
        {
            shouldDisplay = false;
            worldCanvas.enabled = false;
            Debug.LogWarning(
                "PlayerStaminaBar UI references are incomplete. Assign the Canvas, Background Image, and Fill Image in the Inspector.",
                this);
            return;
        }

        backgroundImage.raycastTarget = false;
        fillImage.raycastTarget = false;
    }

    private void RefreshBar(bool snapToValue)
    {
        if (fillImage == null || backgroundImage == null || playerController == null)
            return;

        float max = Mathf.Max(1f, playerController.MaxStaminaForHUD);
        float normalized = Mathf.Clamp01(playerController.Stamina / max);

        if (snapToValue || firstRefresh)
        {
            fillImage.fillAmount = normalized;
            firstRefresh = false;
        }
        else
        {
            fillImage.fillAmount = Mathf.MoveTowards(
                fillImage.fillAmount,
                normalized,
                fillSmoothing * Time.unscaledDeltaTime
            );
        }

        if (normalized <= criticalThreshold)
            fillImage.color = lowStaminaColor;
        else if (normalized <= warningThreshold)
            fillImage.color = mediumStaminaColor;
        else
            fillImage.color = highStaminaColor;

        // At zero, tint the track red so the empty state is obvious even with no fill.
        backgroundImage.color = normalized <= 0.001f
            ? emptyBackgroundColor
            : normalBackgroundColor;
    }

}
