using Fusion;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays a world-space stamina bar above a networked player.
/// By default, each client sees the bar above their own player only.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkPlayerController))]
public class PlayerStaminaBar : NetworkBehaviour
{
    [Header("World-space position and size")]
    [SerializeField, Min(0.5f)] private float heightAbovePlayer = 2.25f;
    [SerializeField] private Vector2 canvasPixelSize = new Vector2(180f, 22f);
    [SerializeField, Min(0.001f)] private float worldScale = 0.006f;
    [SerializeField] private bool showOnlyForLocalPlayer = true;

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
    private Canvas worldCanvas;
    private RectTransform canvasRect;
    private Image backgroundImage;
    private Image fillImage;
    private Camera mainCamera;
    private bool shouldDisplay;
    private bool firstRefresh = true;

    public override void Spawned()
    {
        playerController = GetComponent<NetworkPlayerController>();
        mainCamera = Camera.main;

        shouldDisplay = !showOnlyForLocalPlayer ||
                        (Object != null && Object.HasInputAuthority);

        CreateWorldSpaceBar();

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

    private void CreateWorldSpaceBar()
    {
        if (worldCanvas != null)
            return;

        GameObject canvasObject = new GameObject(
            "StaminaBar_WorldSpace",
            typeof(RectTransform),
            typeof(Canvas)
        );

        canvasObject.transform.SetParent(transform, false);
        canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = canvasPixelSize;
        canvasRect.localPosition = Vector3.up * heightAbovePlayer;
        canvasRect.localScale = Vector3.one * worldScale;

        worldCanvas = canvasObject.GetComponent<Canvas>();
        worldCanvas.renderMode = RenderMode.WorldSpace;
        worldCanvas.sortingOrder = 50;
        worldCanvas.pixelPerfect = false;

        // Dark track behind the stamina fill.
        GameObject backgroundObject = new GameObject(
            "Background",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image)
        );
        backgroundObject.transform.SetParent(canvasRect, false);

        RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
        StretchToParent(backgroundRect);

        backgroundImage = backgroundObject.GetComponent<Image>();
        backgroundImage.color = normalBackgroundColor;
        backgroundImage.raycastTarget = false;

        // Slightly inset fill so the dark track forms a visible border.
        GameObject fillObject = new GameObject(
            "Fill",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image)
        );
        fillObject.transform.SetParent(backgroundRect, false);

        RectTransform fillRect = fillObject.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(3f, 3f);
        fillRect.offsetMax = new Vector2(-3f, -3f);

        fillImage = fillObject.GetComponent<Image>();
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImage.fillAmount = 1f;
        fillImage.color = highStaminaColor;
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

    private static void StretchToParent(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }
}
