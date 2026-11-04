using UnityEngine;

namespace MangoThiefAI
{
    public sealed class ThiefSearchMangoState : ThiefState
    {
        private float nextSearch;
        public override ThiefStateId Id => ThiefStateId.SearchMango;
        public ThiefSearchMangoState(ThiefAI ai) : base(ai) { }
        public override void Enter() => nextSearch = 0f;
        public override void Tick(float deltaTime)
        {
            if (!AI.enableMangoMission || AI.followTestWaypoints || AI.Inventory.IsFull) { AI.ReturnToIdle(); return; }
            AI.transform.Rotate(Vector3.up, AI.searchTurnSpeed * deltaTime);
            if (Time.time < nextSearch) return;
            nextSearch = Time.time + Mathf.Max(0.1f, AI.searchInterval);
            if (!AI.Tactics.TryMango(out MangoTree tree)) return;
            AI.TargetMango = tree;
            AI.EnterState(ThiefStateId.MoveToMango);
        }
    }
    public sealed class ThiefMoveToMangoState : ThiefState
    {
        public override ThiefStateId Id => ThiefStateId.MoveToMango;
        public ThiefMoveToMangoState(ThiefAI ai) : base(ai) { }
        public override void Enter()
        {
            if (AI.TargetMango != null) AI.Navigate(AI.TargetMango.HarvestPosition, false);
        }
        public override void Tick(float deltaTime)
        {
            MangoTree tree = AI.TargetMango;
            if (tree == null || !tree.HasMangoes) { AI.EnterState(ThiefStateId.SearchMango); return; }
            if (Vector3.Distance(AI.Motor.FeetPosition, tree.HarvestPosition) <= tree.interactionRange)
            { AI.EnterState(ThiefStateId.StealMango); return; }
            if (AI.Motor.Status == ThiefMoveStatus.Moving) return;
            AI.Tactics.Exclude(tree, 8f);
            AI.EnterState(ThiefStateId.SearchMango);
        }
    }
    public sealed class ThiefStealMangoState : ThiefState
    {
        private float elapsed;
        public override ThiefStateId Id => ThiefStateId.StealMango;
        public ThiefStealMangoState(ThiefAI ai) : base(ai) { }
        public override void Enter() => elapsed = 0f;
        public override void Tick(float deltaTime)
        {
            MangoTree tree = AI.TargetMango;
            if (tree == null || !tree.HasMangoes || AI.Inventory.IsFull)
            { AI.EnterState(ThiefStateId.SearchMango); return; }
            if (Vector3.Distance(AI.Motor.FeetPosition, tree.HarvestPosition) > tree.interactionRange)
            { AI.EnterState(ThiefStateId.MoveToMango); return; }
            Vector3 direction = tree.transform.position - AI.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.01f)
                AI.transform.rotation = Quaternion.Slerp(AI.transform.rotation, Quaternion.LookRotation(direction), deltaTime * 8f);
            elapsed += deltaTime;
            if (elapsed < Mathf.Max(0.1f, AI.harvestInterval)) return;
            elapsed = 0f;
            if (!tree.TryHarvest(AI.Inventory, AI.Motor.FeetPosition)) AI.EnterState(ThiefStateId.SearchMango);
        }
    }
    public sealed class ThiefHideState : ThiefState
    {
        private float arrivedAt;
        private float nextCoverCheck;
        public override ThiefStateId Id => ThiefStateId.Hide;
        public ThiefHideState(ThiefAI ai) : base(ai) { }
        public override void Enter()
        {
            arrivedAt = -1f;
            nextCoverCheck = Time.time + 1f;
            AI.Navigate(AI.HidePosition, false);
        }
        public override void Tick(float deltaTime)
        {
            if (AI.Motor.Status == ThiefMoveStatus.Failed || AI.Motor.Status == ThiefMoveStatus.None)
            { AI.EnterState(ThiefStateId.Flee); return; }
            if (AI.Motor.Status == ThiefMoveStatus.Arrived && arrivedAt < 0f) arrivedAt = Time.time;
            if (arrivedAt >= 0f && Time.time - arrivedAt >= AI.minimumHideTime && AI.Perception.IsSafeFor(AI.safeWait))
            { AI.EnterState(ThiefStateId.ReturnToSteal); return; }
            if (Time.time < nextCoverCheck || !AI.Perception.OwnerNearby) return;
            nextCoverCheck = Time.time + 1f;
            if (Physics.Linecast(AI.Perception.ThreatPosition + Vector3.up * AI.Perception.ownerEyeHeight,
                AI.HidePosition + Vector3.up * AI.Perception.eyeHeight, AI.Perception.ObstacleMask,
                QueryTriggerInteraction.Ignore)) return;
            if (AI.Tactics.TryHide(out Vector3 point)) { AI.HidePosition = point; Enter(); }
            else AI.EnterState(ThiefStateId.Flee);
        }
    }
    public sealed class ThiefFleeState : ThiefState
    {
        private float nextEscape;
        public override ThiefStateId Id => ThiefStateId.Flee;
        public ThiefFleeState(ThiefAI ai) : base(ai) { }
        public override void Enter() => nextEscape = 0f;
        public override void Tick(float deltaTime)
        {
            if (AI.Perception.IsSafeFor(AI.safeWait)) { AI.EnterState(ThiefStateId.ReturnToSteal); return; }
            if (!AI.Perception.DogChasing && !AI.Perception.OwnerLookingAtMe
                && Vector3.Distance(AI.transform.position, AI.Perception.ThreatPosition) >= AI.Tactics.safeDistance)
            { AI.Motor.Stop(); return; }
            if (Time.time < nextEscape) return;
            nextEscape = Time.time + Mathf.Max(0.2f, AI.escapeDecisionInterval);
            if (AI.Tactics.TryFlee(out Vector3 point)) AI.Navigate(point, true);
            else AI.Motor.Stop();
        }
    }
    public sealed class ThiefClimbTreeState : ThiefState
    {
        public override ThiefStateId Id => ThiefStateId.ClimbTree;
        public ThiefClimbTreeState(ThiefAI ai) : base(ai) { }
        public override void Enter()
        {
            if (!AI.Climber.ControlsMovement) AI.Climber.ClearFailure();
            if (!AI.Climber.ControlsMovement && AI.TargetClimbTree != null && AI.TargetClimbTree.IsConfigured)
                AI.Navigate(AI.TargetClimbTree.climbStartPoint.position, true);
        }
        public override void Tick(float deltaTime)
        {
            if (AI.Climber.ControlsMovement)
            {
                if (AI.Climber.IsPerched) AI.EnterState(ThiefStateId.TreeHide);
                return;
            }
            ClimbableTree tree = AI.TargetClimbTree;
            if (AI.Climber.Failed || tree == null || !tree.IsAvailableFor(AI.Climber)
                || (AI.Perception.OwnerObserved && Vector3.Distance(tree.climbStartPoint.position,
                    AI.Perception.OwnerPosition) < AI.Tactics.safeDistance * 0.5f))
            { GiveUp(); return; }
            if (Vector3.Distance(AI.Motor.FeetPosition, tree.climbStartPoint.position) <= AI.Climber.mountDistance)
            {
                if (!AI.Climber.BeginUp(tree)) GiveUp();
            }
            else if (AI.Motor.Status != ThiefMoveStatus.Moving) GiveUp();
        }
        private void GiveUp()
        {
            AI.NextClimbAttempt = Time.time + AI.climbRetryCooldown;
            AI.EnterState(ThiefStateId.Flee);
        }
        public override void Exit()
        {
            if (!AI.Climber.ControlsMovement && AI.TargetClimbTree != null)
                AI.TargetClimbTree.Release(AI.Climber);
        }
    }
    public sealed class ThiefTreeHideState : ThiefState
    {
        private float entered;
        private float nextDescent;
        public override ThiefStateId Id => ThiefStateId.TreeHide;
        public ThiefTreeHideState(ThiefAI ai) : base(ai) { }
        public override void Enter() { entered = Time.time; nextDescent = Time.time + 1f; }
        public override void Tick(float deltaTime)
        {
            if (!AI.Climber.ControlsMovement) { AI.EnterState(ThiefStateId.Flee); return; }
            AI.transform.Rotate(Vector3.up, AI.searchTurnSpeed * deltaTime);
            if (AI.Climber.IsDescending) { AI.EnterState(ThiefStateId.ClimbDown); return; }
            if (Time.time - entered < AI.minimumTreeTime || Time.time < nextDescent
                || AI.Perception.DogNearby || AI.Perception.OwnerNearby || !AI.Perception.IsSafeFor(AI.safeWait)) return;
            nextDescent = Time.time + AI.climbRetryCooldown;
            if (AI.Climber.BeginDown()) AI.EnterState(ThiefStateId.ClimbDown);
        }
    }
    public sealed class ThiefClimbDownState : ThiefState
    {
        public override ThiefStateId Id => ThiefStateId.ClimbDown;
        public ThiefClimbDownState(ThiefAI ai) : base(ai) { }
        public override void Tick(float deltaTime)
        {
            if (!AI.Climber.ControlsMovement)
                AI.EnterState(AI.Perception.Danger == ThiefDanger.Safe ? ThiefStateId.ReturnToSteal : ThiefStateId.Flee);
            else if (AI.Climber.Tree != null && AI.Climber.Tree.IsConfigured
                && AI.Perception.DogNearby && AI.transform.position.y > AI.Climber.LandingPosition.y + 1f)
            {
                AI.Climber.ReverseUp();
                AI.EnterState(ThiefStateId.ClimbTree);
            }
        }
    }
    public sealed class ThiefReturnToStealState : ThiefState
    {
        private float elapsed;
        public override ThiefStateId Id => ThiefStateId.ReturnToSteal;
        public ThiefReturnToStealState(ThiefAI ai) : base(ai) { }
        public override void Enter() => elapsed = 0f;
        public override void Tick(float deltaTime)
        {
            elapsed += deltaTime;
            if (elapsed >= 0.6f) { AI.TargetMango = null; AI.ReturnToIdle(); }
        }
    }
}
