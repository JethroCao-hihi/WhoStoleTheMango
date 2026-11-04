using System.Collections.Generic;
using UnityEngine;

namespace MangoThiefAI
{
    public enum ThiefDanger { Safe, Suspicious, Detected, DogPursuit }

    /// <summary>Only records positions after sight/hearing; other modules consume these snapshots.</summary>
    [DisallowMultipleComponent]
    public sealed class ThiefPerception : MonoBehaviour
    {
        [Min(0.05f)] public float scanInterval = 0.2f;
        [Min(1f)] public float sightRadius = 18f;
        [Range(10f, 360f)] public float fieldOfView = 220f;
        [Min(0f)] public float hearingRadius = 8f;
        [Min(0f)] public float cautionRadius = 11f;
        [Min(0f)] public float ownerSightRadius = 16f;
        [Range(10f, 360f)] public float ownerFieldOfView = 100f;
        [Min(0f)] public float detectionDelay = 0.7f;
        [Min(0f)] public float immediateDetectionRange = 2f;
        [Min(0.1f)] public float dangerMemory = 5f;
        [Min(0.1f)] public float dogMemory = 3f;
        [Min(0f)] public float dogSafetyRadius = 7f;
        [Min(0f)] public float eyeHeight = 1.4f;
        [Min(0f)] public float ownerEyeHeight = 1.6f;
        public LayerMask sensingMask = ~0;
        public bool drawGizmos = true;
        public ThiefDanger Danger { get; private set; }
        public bool OwnerNearby { get; private set; }
        public bool OwnerObserved { get; private set; }
        public bool OwnerLookingAtMe { get; private set; }
        public bool OwnerConfirmed => Time.time < confirmedUntil;
        public bool DogChasing => Time.time < dogUntil;
        public bool DogNearby => Time.time < nearbyDogUntil;
        public bool HasThreatMemory => Time.time < rememberedUntil;
        public Vector3 ThreatPosition { get; private set; }
        public Vector3 OwnerPosition { get; private set; }
        public Vector3 OwnerForward { get; private set; }
        public Vector3 DogPosition { get; private set; }
        public GardenDog ObservedDog { get; private set; }
        public float SafeSince { get; private set; }
        public LayerMask ObstacleMask => motor != null && motor.navigationGrid != null
            ? motor.navigationGrid.obstacleMask : (LayerMask)0;
        private readonly Collider[] overlaps = new Collider[128];
        private readonly HashSet<int> checkedRoots = new HashSet<int>();
        private GardenPlayer player;
        private ThiefMotor motor;
        private gardenOwner focusedOwner;
        private float nextScan;
        private float exposure;
        private float confirmedUntil;
        private float dogUntil;
        private float nearbyDogUntil;
        private float rememberedUntil;
        private float previousScan;

        private void Awake()
        {
            player = GetComponent<GardenPlayer>();
            motor = GetComponent<ThiefMotor>();
            SafeSince = Time.time;
        }
        public bool IsSafeFor(float seconds) => Danger == ThiefDanger.Safe && !DogNearby
            && Time.time - SafeSince >= seconds;
        public bool CanObserve(Vector3 point)
        {
            Vector3 eyes = transform.position + Vector3.up * eyeHeight;
            Vector3 direction = point - eyes;
            return direction.sqrMagnitude <= sightRadius * sightRadius
                && Vector3.Angle(transform.forward, direction) <= fieldOfView * 0.5f
                && !Physics.Linecast(eyes, point, ObstacleMask, QueryTriggerInteraction.Ignore);
        }
        public void RememberDamage()
        {
            if (!HasThreatMemory) ThreatPosition = transform.position + transform.forward * 2f;
            rememberedUntil = confirmedUntil = Time.time + dangerMemory;
            SafeSince = Time.time;
        }
        public void Tick()
        {
            if (Time.time < nextScan) return;
            float elapsed = Mathf.Clamp(Time.time - previousScan, 0f, scanInterval * 2f);
            previousScan = Time.time;
            nextScan = Time.time + Mathf.Max(0.05f, scanInterval);
            OwnerNearby = OwnerLookingAtMe = OwnerObserved = false;
            ObservedDog = null;
            gardenOwner bestOwner = null;
            float bestOwnerScore = float.NegativeInfinity;
            bool bestLooking = false;
            bool bestImmediate = false;
            float closestDog = float.PositiveInfinity;
            checkedRoots.Clear();
            float radius = Mathf.Max(sightRadius, hearingRadius);
            int count = Physics.OverlapSphereNonAlloc(transform.position, radius, overlaps,
                sensingMask, QueryTriggerInteraction.Ignore);
            foreach (Collider collider in overlaps)
            {
                // Only the populated prefix is valid; the buffer is reused without per-scan allocation.
                if (count-- <= 0) break;
                if (collider == null) continue;
                gardenOwner owner = collider.GetComponentInParent<gardenOwner>();
                if (owner != null)
                {
                    if (!owner.isActiveAndEnabled || !checkedRoots.Add(owner.GetInstanceID())) continue;
                    GardenPlayer health = owner.GetComponent<GardenPlayer>();
                    if (health == null || !health.IsAlive) continue;
                    Vector3 position = owner.transform.position;
                    float distance = Vector3.Distance(transform.position, position);
                    CharacterController cc = owner.GetComponent<CharacterController>();
                    bool heard = distance <= hearingRadius && cc != null && cc.velocity.sqrMagnitude > 0.05f;
                    if (!CanObserve(position + Vector3.up * ownerEyeHeight) && !heard) continue;
                    Vector3 toMe = transform.position + Vector3.up * eyeHeight
                        - (position + Vector3.up * ownerEyeHeight);
                    bool looking = distance <= ownerSightRadius
                        && Vector3.Angle(owner.transform.forward, toMe) <= ownerFieldOfView * 0.5f
                        && !Physics.Linecast(position + Vector3.up * ownerEyeHeight,
                            transform.position + Vector3.up * eyeHeight, ObstacleMask, QueryTriggerInteraction.Ignore);
                    bool immediate = distance <= immediateDetectionRange
                        && !Physics.Linecast(position + Vector3.up * ownerEyeHeight,
                            transform.position + Vector3.up * eyeHeight, ObstacleMask, QueryTriggerInteraction.Ignore);
                    float score = (looking || immediate ? 100f : 0f) - distance;
                    if (score <= bestOwnerScore) continue;
                    bestOwnerScore = score;
                    bestOwner = owner;
                    bestLooking = looking;
                    bestImmediate = immediate;
                }
                else
                {
                    GardenDog dog = collider.GetComponentInParent<GardenDog>();
                    if (dog == null || !dog.isActiveAndEnabled || !checkedRoots.Add(dog.GetInstanceID())) continue;
                    float distance = Vector3.Distance(transform.position, dog.transform.position);
                    bool chasing = dog.Target == player;
                    // A pursuing dog can be heard barking even when stationary under a tree.
                    if (!CanObserve(dog.transform.position + Vector3.up * 0.5f)
                        && !(chasing && distance <= hearingRadius)) continue;
                    if (distance <= dogSafetyRadius) nearbyDogUntil = Time.time + scanInterval * 2f;
                    if (!chasing || distance >= closestDog) continue;
                    closestDog = distance;
                    ObservedDog = dog;
                    DogPosition = dog.transform.position;
                    dogUntil = Time.time + dogMemory;
                }
            }
            if (bestOwner != null)
            {
                OwnerObserved = true;
                if (focusedOwner != bestOwner) exposure = 0f;
                focusedOwner = bestOwner;
                OwnerPosition = bestOwner.transform.position;
                OwnerForward = bestOwner.transform.forward;
                OwnerNearby = Vector3.Distance(transform.position, OwnerPosition) <= cautionRadius;
                OwnerLookingAtMe = bestLooking;
                exposure = bestLooking ? exposure + elapsed : 0f;
                if (bestImmediate || (bestLooking && exposure >= detectionDelay))
                    confirmedUntil = Time.time + dangerMemory;
                if (OwnerNearby || OwnerConfirmed)
                {
                    ThreatPosition = OwnerPosition;
                    rememberedUntil = Time.time + dangerMemory;
                }
            }
            else { exposure = 0f; focusedOwner = null; }
            if (DogChasing)
            {
                ThreatPosition = DogPosition;
                rememberedUntil = Mathf.Max(rememberedUntil, dogUntil);
            }
            Danger = DogChasing ? ThiefDanger.DogPursuit : OwnerConfirmed ? ThiefDanger.Detected
                : OwnerNearby || HasThreatMemory ? ThiefDanger.Suspicious : ThiefDanger.Safe;
            if (Danger != ThiefDanger.Safe || DogNearby) SafeSince = Time.time;
        }
        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, sightRadius);
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, hearingRadius);
            Vector3 eyes = transform.position + Vector3.up * eyeHeight;
            Gizmos.DrawRay(eyes, Quaternion.Euler(0f, fieldOfView * 0.5f, 0f) * transform.forward * sightRadius);
            Gizmos.DrawRay(eyes, Quaternion.Euler(0f, -fieldOfView * 0.5f, 0f) * transform.forward * sightRadius);
            if (Application.isPlaying && HasThreatMemory)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(ThreatPosition, 0.5f);
                Gizmos.DrawLine(eyes, ThreatPosition);
            }
        }
    }
}
