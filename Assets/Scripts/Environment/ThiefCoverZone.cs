using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Add to a trigger volume inside a bush or tall grass. A thief becomes hidden-in-cover
/// while crouching inside this volume. This is a gameplay state, not visual invisibility.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider), typeof(Rigidbody))]
public sealed class ThiefCoverZone : MonoBehaviour
{
    [SerializeField] private bool showDebugLogs = true;

    private Collider zoneCollider;
    private Rigidbody zoneRigidbody;
    private readonly HashSet<NetworkPlayerController> trackedPlayers =
        new HashSet<NetworkPlayerController>();

    private void Reset()
    {
        ConfigureTrigger();
    }

    private void Awake()
    {
        ConfigureTrigger();
    }

    private void ConfigureTrigger()
    {
        zoneCollider = GetComponent<Collider>();
        if (zoneCollider == null)
        {
            Debug.LogError("[ThiefCoverZone] Missing Collider on cover volume.", this);
            enabled = false;
            return;
        }

        zoneCollider.isTrigger = true;

        zoneRigidbody = GetComponent<Rigidbody>();
        if (zoneRigidbody == null)
            zoneRigidbody = gameObject.AddComponent<Rigidbody>();

        zoneRigidbody.isKinematic = true;
        zoneRigidbody.useGravity = false;
        zoneRigidbody.detectCollisions = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        RegisterPlayer(other);
    }

    // Stay acts as a safety net if a networked player spawns or is enabled inside the volume.
    private void OnTriggerStay(Collider other)
    {
        RegisterPlayer(other);
    }

    private void RegisterPlayer(Collider other)
    {
        NetworkPlayerController controller = other.GetComponentInParent<NetworkPlayerController>();
        if (controller == null || !trackedPlayers.Add(controller))
            return;

        controller.EnterCoverZone(this);
        if (showDebugLogs)
            Debug.Log($"[ThiefCoverZone] {controller.name} entered cover volume '{name}'.", this);
    }

    private void OnTriggerExit(Collider other)
    {
        NetworkPlayerController controller = other.GetComponentInParent<NetworkPlayerController>();
        if (controller == null || !trackedPlayers.Remove(controller))
            return;

        controller.ExitCoverZone(this);
        if (showDebugLogs)
            Debug.Log($"[ThiefCoverZone] {controller.name} exited cover volume '{name}'.", this);
    }

    private void OnDisable()
    {
        foreach (NetworkPlayerController controller in trackedPlayers)
        {
            if (controller != null)
                controller.ExitCoverZone(this);
        }
        trackedPlayers.Clear();
    }

    private void OnDrawGizmosSelected()
    {
        Collider col = GetComponent<Collider>();
        if (col == null)
            return;

        Gizmos.color = new Color(0.1f, 1f, 0.25f, 0.25f);
        Gizmos.matrix = transform.localToWorldMatrix;
        if (col is BoxCollider box)
            Gizmos.DrawCube(box.center, box.size);
        else if (col is SphereCollider sphere)
            Gizmos.DrawSphere(sphere.center, sphere.radius);
    }
}
