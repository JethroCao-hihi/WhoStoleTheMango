using Fusion;
using UnityEngine;

public enum PlayerNoiseType
{
    None = 0,
    WalkStep = 1,
    RunStep = 2,
    CrouchStep = 3,
    Harvest = 4,
    Splash = 5,
    Jump = 6,
    Landing = 7,
    Climb = 8,
    MangoDrop = 9,
    Capture = 10
}

/// <summary>
/// Networked noise events emitted by the thief's actions.
/// Owner-side listening/detection is intentionally implemented by a separate feature branch.
/// Optional AudioClips can be assigned when the project has sound assets.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class NetworkPlayerSoundState : NetworkBehaviour
{
    [Header("Optional audio assets (can be assigned later)")]
    [SerializeField] private AudioClip walkClip;
    [SerializeField] private AudioClip runClip;
    [SerializeField] private AudioClip crouchClip;
    [SerializeField] private AudioClip harvestClip;
    [SerializeField] private AudioClip splashClip;
    [SerializeField] private AudioClip jumpClip;
    [SerializeField] private AudioClip climbClip;
    [SerializeField] private AudioClip dropClip;
    [SerializeField] private AudioClip captureClip;

    // Only event ID/type are replicated for optional audio playback. Exact sound location and
    // radius stay server-local so thief clients cannot read their own exact source positions.
    [Networked] public int NoiseSequence { get; private set; }
    [Networked] public int LastNoiseTypeCode { get; private set; }

    private Vector3 lastNoisePosition;
    private float lastNoiseRadius;
    private float lastNoiseTime;
    private AudioSource audioSource;

    public Vector3 LastNoisePosition => lastNoisePosition;
    public float LastNoiseRadius => lastNoiseRadius;
    public float LastNoiseTime => lastNoiseTime;
    private int lastPlayedSequence = -1;

    public PlayerNoiseType LastNoiseType => (PlayerNoiseType)LastNoiseTypeCode;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.minDistance = 1f;
        audioSource.maxDistance = 22f;
    }

    public void EmitNoise(PlayerNoiseType type, float radius)
    {
        if (Object == null || !Object.HasStateAuthority || Runner == null || type == PlayerNoiseType.None)
            return;

        NoiseSequence++;
        LastNoiseTypeCode = (int)type;
        lastNoisePosition = transform.position;
        lastNoiseRadius = Mathf.Max(0f, radius);
        lastNoiseTime = (float)Runner.SimulationTime;
    }

    /// <summary>Returns true if this recent event can be heard at a listener location.</summary>
    public bool IsAudibleAt(Vector3 listenerPosition, double simulationTime, out Vector3 direction)
    {
        direction = Vector3.zero;
        if (LastNoiseType == PlayerNoiseType.None || lastNoiseRadius <= 0f)
            return false;

        double age = simulationTime - lastNoiseTime;
        if (age < 0f || age > 1.5f)
            return false;

        Vector3 offset = lastNoisePosition - listenerPosition;
        if (offset.sqrMagnitude > lastNoiseRadius * lastNoiseRadius)
            return false;

        direction = offset.sqrMagnitude > 0.001f ? offset.normalized : Vector3.zero;
        return true;
    }

    public override void Render()
    {
        if (NoiseSequence == lastPlayedSequence)
            return;

        lastPlayedSequence = NoiseSequence;
        AudioClip clip = GetClip(LastNoiseType);
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }

    private AudioClip GetClip(PlayerNoiseType type)
    {
        switch (type)
        {
            case PlayerNoiseType.WalkStep: return walkClip;
            case PlayerNoiseType.RunStep: return runClip;
            case PlayerNoiseType.CrouchStep: return crouchClip;
            case PlayerNoiseType.Harvest: return harvestClip;
            case PlayerNoiseType.Splash: return splashClip;
            case PlayerNoiseType.Jump:
            case PlayerNoiseType.Landing: return jumpClip;
            case PlayerNoiseType.Climb: return climbClip;
            case PlayerNoiseType.MangoDrop: return dropClip;
            case PlayerNoiseType.Capture: return captureClip;
            default: return null;
        }
    }
}
