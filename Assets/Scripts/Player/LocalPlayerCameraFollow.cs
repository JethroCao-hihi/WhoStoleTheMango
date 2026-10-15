
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(1000)]
public class LocalPlayerCameraFollow : MonoBehaviour
{
    public static float CurrentYaw { get; private set; }

    [Header("Mouse Look")]
    [SerializeField] private float mouseSensitivity = 0.12f;
    [SerializeField] private float minPitch = -25f;
    [SerializeField] private float maxPitch = 65f;

    [Header("Third Person Camera")]
    [SerializeField] private float cameraDistance = 5.5f;
    [SerializeField] private float targetHeight = 1.3f;
    [SerializeField] private float shoulderOffset = 0.35f;

    [Header("Anti-Jitter")]
    [Tooltip("Giảm rung vị trí camera khi Transform cập nhật từ network.")]
    [SerializeField] private float followSmoothTime = 0.045f;

    [Header("Camera Collision")]
    [Tooltip("Chỉ những Layer được chọn mới có thể chặn camera.")]
    [SerializeField] private LayerMask collisionLayers = ~0;

    [SerializeField] private float collisionRadius = 0.25f;
    [SerializeField] private float collisionBuffer = 0.08f;

    [Tooltip("Tốc độ camera lùi ra khi hết vật cản.")]
    [SerializeField] private float collisionReturnSpeed = 8f;

    [Header("Cursor")]
    [SerializeField] private bool ignoreHostJoinPanel = true;

    private Transform target;

    private float yaw;
    private float pitch = 12f;
    private float nextSearchTime;

    private Vector3 smoothedPivot;
    private Vector3 pivotVelocity;

    private float currentDistance;
    private bool cameraInitialized;

    // Dùng lại mảng để tránh cấp phát bộ nhớ mỗi frame.
    private readonly RaycastHit[] collisionHits = new RaycastHit[32];

    private void Awake()
    {
        yaw = transform.eulerAngles.y;
        CurrentYaw = yaw;
        currentDistance = cameraDistance;
    }

    private void Update()
    {
        HandleCursor();
        HandleMouseLook();

        CurrentYaw = yaw;

        if (target == null && Time.unscaledTime >= nextSearchTime)
        {
            nextSearchTime = Time.unscaledTime + 0.25f;
            FindLocalPlayer();
        }
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        float deltaTime = Time.unscaledDeltaTime;

        if (deltaTime <= 0f)
            return;

        // Pivot là vị trí camera quan sát, không phụ thuộc góc xoay
        // của model Player. Điều này tránh camera bị quăng ngang
        // mỗi khi nhân vật quay theo hướng di chuyển.
        Vector3 rawPivot =
            target.position + Vector3.up * targetHeight;

        rawPivot +=
            Quaternion.Euler(0f, yaw, 0f) *
            new Vector3(shoulderOffset, 0f, 0f);

        if (!cameraInitialized)
        {
            smoothedPivot = rawPivot;
            pivotVelocity = Vector3.zero;
            currentDistance = cameraDistance;
            cameraInitialized = true;
        }

        // Làm mượt chuyển động mục tiêu giữa các lần cập nhật.
        smoothedPivot = Vector3.SmoothDamp(
            smoothedPivot,
            rawPivot,
            ref pivotVelocity,
            Mathf.Max(0.001f, followSmoothTime),
            Mathf.Infinity,
            deltaTime
        );

        Quaternion orbitRotation =
            Quaternion.Euler(pitch, yaw, 0f);

        Vector3 cameraDirection =
            orbitRotation * Vector3.back;

        // Tính khoảng cách an toàn trước khi đặt camera.
        float safeDistance = GetSafeCameraDistance(
            smoothedPivot,
            cameraDirection
        );

        // Lùi vào ngay khi gặp vật cản; chỉ từ từ lùi ra
        // khi đường nhìn đã thông thoáng.
        if (safeDistance < currentDistance)
        {
            currentDistance = safeDistance;
        }
        else
        {
            currentDistance = Mathf.MoveTowards(
                currentDistance,
                safeDistance,
                collisionReturnSpeed * deltaTime
            );
        }

        transform.position =
            smoothedPivot + cameraDirection * currentDistance;

        Vector3 lookDirection =
            smoothedPivot - transform.position;

        if (lookDirection.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(
                lookDirection.normalized,
                Vector3.up
            );
        }
    }

    private float GetSafeCameraDistance(
        Vector3 pivot,
        Vector3 direction)
    {
        float safeDistance = cameraDistance;

        int hitCount = Physics.SphereCastNonAlloc(
            pivot,
            collisionRadius,
            direction,
            collisionHits,
            cameraDistance,
            collisionLayers,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = collisionHits[i];
            Collider hitCollider = hit.collider;

            if (hitCollider == null)
                continue;

            // Không để collider của chính Player đẩy camera vào trong.
            Transform hitTransform = hitCollider.transform;

            if (target != null &&
                (hitTransform == target ||
                 hitTransform.IsChildOf(target)))
            {
                continue;
            }

            if (hitCollider.isTrigger)
                continue;

            float candidateDistance = Mathf.Max(
                0.05f,
                hit.distance - collisionBuffer
            );

            if (candidateDistance < safeDistance)
                safeDistance = candidateDistance;
        }

        return safeDistance;
    }

    private void HandleMouseLook()
    {
        if (Mouse.current == null)
            return;

        if (Cursor.lockState != CursorLockMode.Locked)
            return;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        yaw += mouseDelta.x * mouseSensitivity;
        pitch -= mouseDelta.y * mouseSensitivity;

        yaw = Mathf.Repeat(yaw, 360f);
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
    }

    private void HandleCursor()
    {
        if (Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            UnlockCursor();
            return;
        }

        if (Keyboard.current != null &&
            Keyboard.current.tabKey.wasPressedThisFrame)
        {
            if (Cursor.lockState == CursorLockMode.Locked)
                UnlockCursor();
            else
                LockCursor();

            return;
        }

        if (Mouse.current == null ||
            !Mouse.current.leftButton.wasPressedThisFrame)
        {
            return;
        }

        if (ignoreHostJoinPanel && IsOverHostJoinPanel())
            return;

        LockCursor();
    }

    private bool IsOverHostJoinPanel()
    {
        if (Mouse.current == null)
            return false;

        Vector2 mousePosition =
            Mouse.current.position.ReadValue();

        // OnGUI của project hiện đặt bảng Host/Join ở góc trên trái.
        return mousePosition.x >= 0f &&
               mousePosition.x <= 340f &&
               mousePosition.y >= Screen.height - 250f &&
               mousePosition.y <= Screen.height;
    }

    private void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
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

            target = player.transform;

            yaw = target.eulerAngles.y;
            CurrentYaw = yaw;

            cameraInitialized = false;
            pivotVelocity = Vector3.zero;

            Debug.Log(
                $"[Camera] Following local player: {player.name}",
                player
            );

            return;
        }
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;

        if (target != null)
        {
            yaw = target.eulerAngles.y;
            CurrentYaw = yaw;
        }

        cameraInitialized = false;
        pivotVelocity = Vector3.zero;
    }
}
