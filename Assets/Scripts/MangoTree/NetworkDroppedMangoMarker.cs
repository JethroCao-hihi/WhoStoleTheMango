using Fusion;
using UnityEngine;

/// <summary>
/// Marks a network mango as a fruit dropped onto the ground and animates its toss.
/// Put this on the same root as NetworkObject, NetworkTransform and NetworkMango.
/// Only StateAuthority advances the arc; NetworkTransform replicates its position/rotation.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkTransform))]
public class NetworkDroppedMangoMarker : NetworkBehaviour
{
    [Header("Drop animation")]
    [SerializeField, Min(0.1f)] private float dropDuration = 0.48f;
    [SerializeField, Min(0f)] private float arcHeight = 0.75f;
    [SerializeField] private Vector3 spinDegrees = new Vector3(500f, 720f, 360f);

    [Networked] public NetworkBool IsDropped { get; private set; }
    [Networked] public NetworkBool IsAnimatingDrop { get; private set; }
    [Networked] private Vector3 DropStartPosition { get; set; }
    [Networked] private Vector3 DropLandingPosition { get; set; }
    [Networked] private Vector3 DropStartEuler { get; set; }
    [Networked] private float DropElapsed { get; set; }
    [Networked] private float ActiveDropDuration { get; set; }

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            IsDropped = false;
            IsAnimatingDrop = false;
            DropElapsed = 0f;
        }

        if (GetComponent<NetworkTransform>() == null)
            Debug.LogError("[Mango Drop] Thiếu NetworkTransform nên chuyển động xoài không thể đồng bộ.", this);
    }

    /// <summary>Begin a visible toss to the specified ground position using the prefab default duration.</summary>
    public void MarkDropped(Vector3 landingPosition)
    {
        MarkDropped(landingPosition, dropDuration);
    }

    /// <summary>Begin a visible toss with an optional duration (longer when dropping from a tree).</summary>
    public void MarkDropped(Vector3 landingPosition, float durationSeconds)
    {
        if (Object == null || !Object.HasStateAuthority)
            return;

        ActiveDropDuration = Mathf.Max(0.1f, durationSeconds);
        IsDropped = true;
        DropStartPosition = transform.position;
        DropLandingPosition = landingPosition;
        DropStartEuler = transform.eulerAngles;
        DropElapsed = 0f;
        IsAnimatingDrop = Vector3.Distance(DropStartPosition, DropLandingPosition) > 0.03f;

        if (!IsAnimatingDrop)
            transform.position = DropLandingPosition;

        Debug.Log($"[Mango Drop Animation] Start={DropStartPosition} | Landing={DropLandingPosition} | Animated={IsAnimatingDrop}", this);
    }

    // Compatibility overload for other scripts that only need to mark a mango as dropped.
    public void MarkDropped()
    {
        MarkDropped(transform.position);
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority || !IsDropped || !IsAnimatingDrop)
            return;

        float duration = Mathf.Max(0.1f, ActiveDropDuration);
        DropElapsed = Mathf.Min(DropElapsed + Runner.DeltaTime, duration);
        float t = Mathf.Clamp01(DropElapsed / duration);

        Vector3 position = Vector3.Lerp(DropStartPosition, DropLandingPosition, t);
        position.y += Mathf.Sin(t * Mathf.PI) * arcHeight;
        transform.position = position;

        Vector3 spin = spinDegrees * (DropElapsed / duration);
        transform.rotation = Quaternion.Euler(DropStartEuler + spin);

        if (t >= 1f)
        {
            transform.position = DropLandingPosition;
            IsAnimatingDrop = false;
            Debug.Log($"[Mango Drop Animation] Landed at {DropLandingPosition}.", this);
        }
    }
}
