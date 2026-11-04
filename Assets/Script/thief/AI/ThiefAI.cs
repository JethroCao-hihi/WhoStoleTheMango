using System.Collections.Generic;
using UnityEngine;

namespace MangoThiefAI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GardenPlayer), typeof(ThiefMotor), typeof(ThiefAnimationDriver))]
    [RequireComponent(typeof(ThiefPerception), typeof(ThiefClimber), typeof(ThiefInventory))]
    [RequireComponent(typeof(ThiefTactics))]
    public sealed class ThiefAI : MonoBehaviour
    {
        [Header("Mission")]
        public bool enableMangoMission = true;
        [Min(0.1f)] public float idleDuration = 1f;
        [Min(0.1f)] public float searchInterval = 0.7f;
        [Min(0f)] public float searchTurnSpeed = 75f;
        [Min(0.1f)] public float harvestInterval = 1.5f;
        [Min(0.1f)] public float safeWait = 3f;
        [Min(0.1f)] public float minimumHideTime = 2f;
        [Min(0.1f)] public float minimumTreeTime = 3f;
        [Min(0.2f)] public float escapeDecisionInterval = 1.5f;
        [Min(0.2f)] public float climbRetryCooldown = 2f;
        [Min(0.05f)] public float decisionInterval = 0.2f;
        [Header("Optional locomotion test (takes priority over the mango mission)")]
        public bool followTestWaypoints;
        public Transform[] testWaypoints = new Transform[0];
        public bool loopWaypoints = true;
        public bool runBetweenWaypoints;
        public bool logStateChanges;
        public bool drawGizmos = true;
        public ThiefMotor Motor { get; private set; }
        public GardenPlayer Player { get; private set; }
        public ThiefPerception Perception { get; private set; }
        public ThiefTactics Tactics { get; private set; }
        public ThiefClimber Climber { get; private set; }
        public ThiefInventory Inventory { get; private set; }
        public ThiefStateId CurrentState => current != null ? current.Id : ThiefStateId.Idle;
        public float IdleDuration => Mathf.Max(0.1f, idleDuration);
        public string LastMoveFailure { get; private set; }
        public MangoTree TargetMango { get; internal set; }
        public ClimbableTree TargetClimbTree { get; internal set; }
        public Vector3 HidePosition { get; internal set; }
        public float NextClimbAttempt { get; internal set; }
        private readonly Dictionary<ThiefStateId, ThiefState> states = new Dictionary<ThiefStateId, ThiefState>();
        private ThiefAnimationDriver animationDriver;
        private CharacterController controller;
        private ThiefState current;
        private int nextWaypoint;
        private float nextDecision;
        private T Ensure<T>() where T : Component
        {
            T component = GetComponent<T>();
            return component != null ? component : gameObject.AddComponent<T>();
        }
        private void Awake()
        {
            Motor = GetComponent<ThiefMotor>();
            Player = GetComponent<GardenPlayer>();
            controller = GetComponent<CharacterController>();
            animationDriver = GetComponent<ThiefAnimationDriver>();
            Perception = Ensure<ThiefPerception>();
            Climber = Ensure<ThiefClimber>();
            Inventory = Ensure<ThiefInventory>();
            Tactics = Ensure<ThiefTactics>();
            Register(new ThiefIdleState(this));
            Register(new ThiefMoveState(this));
            Register(new ThiefSearchMangoState(this));
            Register(new ThiefMoveToMangoState(this));
            Register(new ThiefStealMangoState(this));
            Register(new ThiefHideState(this));
            Register(new ThiefFleeState(this));
            Register(new ThiefClimbTreeState(this));
            Register(new ThiefTreeHideState(this));
            Register(new ThiefClimbDownState(this));
            Register(new ThiefReturnToStealState(this));
            if (GetComponent<gardenOwner>() != null || GetComponent<GardenDog>() != null)
            {
                Debug.LogError("Put ThiefAI on a separate thief root without gardenOwner or GardenDog.", this);
                enabled = false;
            }
        }
        private void Register(ThiefState state) => states.Add(state.Id, state);
        private void OnEnable()
        {
            nextWaypoint = 0;
            nextDecision = 0f;
            if (Player != null) Player.onDamaged.AddListener(OnDamaged);
            EnterState(ThiefStateId.Idle);
        }
        private void OnDamaged() => Perception.RememberDamage();
        private void Update()
        {
            Perception.Tick();
            if (!Player.isActiveAndEnabled || !Player.IsAlive || !Motor.isActiveAndEnabled)
            {
                Climber.Abort();
                ReturnToIdle();
                Motor.Stop();
                if (Motor.isActiveAndEnabled) Motor.Tick(Time.deltaTime, false);
                animationDriver.Tick(0f, false, ThiefStateId.Idle);
                return;
            }
            if (!controller.enabled && !Climber.ControlsMovement)
            {
                ReturnToIdle();
                animationDriver.Tick(0f, false, ThiefStateId.Idle);
                return;
            }
            if (Time.time >= nextDecision)
            {
                nextDecision = Time.time + Mathf.Max(0.05f, decisionInterval);
                ChooseDangerResponse();
            }
            if (Player.CanMove) current?.Tick(Time.deltaTime);
            if (Climber.ControlsMovement) Climber.Tick(Time.deltaTime, Player.CanMove);
            else Motor.Tick(Time.deltaTime, Player.CanMove);
            animationDriver.Tick(Climber.ControlsMovement ? 0f : Motor.ActualSpeed,
                Motor.RequestedRun, CurrentState, Climber.IsTraversing && Player.CanMove);
        }
        private void ChooseDangerResponse()
        {
            if (Climber.ControlsMovement || CurrentState == ThiefStateId.ClimbTree) return;
            if (Perception.DogChasing && Time.time >= NextClimbAttempt)
            {
                NextClimbAttempt = Time.time + climbRetryCooldown;
                if (Tactics.TryClimbTree(out ClimbableTree tree) && tree.TryReserve(Climber))
                {
                    TargetClimbTree = tree;
                    EnterState(ThiefStateId.ClimbTree);
                    return;
                }
            }
            if (Perception.DogChasing || Perception.OwnerConfirmed)
            { EnterState(ThiefStateId.Flee); return; }
            if (Perception.Danger == ThiefDanger.Suspicious
                && CurrentState != ThiefStateId.Hide && CurrentState != ThiefStateId.Flee)
            {
                if (Tactics.TryHide(out Vector3 point))
                { HidePosition = point; EnterState(ThiefStateId.Hide); }
                else EnterState(ThiefStateId.Flee);
            }
        }
        internal void EnterState(ThiefStateId id)
        {
            if (!states.TryGetValue(id, out ThiefState next) || current == next) return;
            current?.Exit();
            Motor.Stop();
            current = next;
            current.Enter();
            if (logStateChanges) Debug.Log("[ThiefAI] State: " + id, this);
        }
        internal bool Navigate(Vector3 destination, bool run)
        {
            if (!Motor.TrySetDestination(destination, run)) { ReportMoveFailure(); return false; }
            LastMoveFailure = null;
            return true;
        }
        public bool TryMoveTo(Vector3 destination, bool run = false)
        {
            if (!isActiveAndEnabled || Motor == null || !Player.isActiveAndEnabled
                || !Player.CanMove || Climber.ControlsMovement) return false;
            EnterState(ThiefStateId.MoveToWaypoint);
            if (Navigate(destination, run)) return true;
            ReturnToIdle();
            return false;
        }
        public void ReturnToIdle()
        {
            // Cancelling a ground command must not strand a perched character in Idle.
            if (Climber != null && Climber.ControlsMovement) return;
            EnterState(ThiefStateId.Idle);
        }
        internal void DecideMission()
        {
            if (followTestWaypoints) TryStartNextWaypoint();
            else if (enableMangoMission && !Inventory.IsFull) EnterState(ThiefStateId.SearchMango);
        }
        internal void TryStartNextWaypoint()
        {
            if (!followTestWaypoints || testWaypoints == null || testWaypoints.Length == 0) return;
            if (nextWaypoint >= testWaypoints.Length)
            {
                if (!loopWaypoints) return;
                nextWaypoint = 0;
            }
            Transform target = testWaypoints[nextWaypoint++];
            if (target != null) TryMoveTo(target.position, runBetweenWaypoints);
        }
        internal void ReportMoveFailure()
        {
            LastMoveFailure = Motor.FailureReason;
            if (logStateChanges) Debug.LogWarning("[ThiefAI] " + LastMoveFailure, this);
        }
        private void OnDisable()
        {
            if (Player != null) Player.onDamaged.RemoveListener(OnDamaged);
            current?.Exit();
            current = null;
            if (Climber != null) Climber.Abort();
            if (Motor != null) Motor.Stop();
            if (animationDriver != null) animationDriver.Tick(0f, false, ThiefStateId.Idle);
        }
        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || testWaypoints == null) return;
            Gizmos.color = new Color(1f, 0.6f, 0f);
            foreach (Transform target in testWaypoints)
                if (target != null) Gizmos.DrawWireSphere(target.position, 0.25f);
        }
    }
}
