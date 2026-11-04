using UnityEngine;

namespace MangoThiefAI
{
    /// <summary>Visual output only. No animation event or clip controls gameplay.</summary>
    [DisallowMultipleComponent]
    public sealed class ThiefAnimationDriver : MonoBehaviour
    {
        public Animator animator;
        [Min(0.001f)] public float movingThreshold = 0.05f;
        private static readonly int Walk = Animator.StringToHash("isWalk");
        private static readonly int Run = Animator.StringToHash("isRun");
        private static readonly int Speed = Animator.StringToHash("Speed");
        private static readonly int Action = Animator.StringToHash("Action");
        private static readonly int State = Animator.StringToHash("AIState");
        private RuntimeAnimatorController cachedController;
        private Animator cachedAnimator;
        private bool hasWalk;
        private bool hasRun;
        private bool hasSpeed;
        private bool hasAction;
        private bool hasState;

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            RefreshParameters();
        }

        private void RefreshParameters()
        {
            cachedAnimator = animator;
            cachedController = animator != null ? animator.runtimeAnimatorController : null;
            hasWalk = hasRun = hasSpeed = hasAction = hasState = false;
            if (cachedController == null) return;
            animator.applyRootMotion = false;
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.type == AnimatorControllerParameterType.Bool)
                {
                    if (parameter.nameHash == Walk) hasWalk = true;
                    if (parameter.nameHash == Run) hasRun = true;
                }
                if (parameter.nameHash == Speed && parameter.type == AnimatorControllerParameterType.Float)
                    hasSpeed = true;
                if (parameter.nameHash == Action && parameter.type == AnimatorControllerParameterType.Int) hasAction = true;
                if (parameter.nameHash == State && parameter.type == AnimatorControllerParameterType.Int) hasState = true;
            }
            if (!hasWalk || !hasRun)
                Debug.LogWarning("Thief Animator needs Bool isWalk and isRun. Use Tools/Thief AI/Create Locomotion Controller.", this);
        }

        public void Tick(float actualSpeed, bool requestedRun, ThiefStateId state = ThiefStateId.Idle, bool climbing = false)
        {
            if (!isActiveAndEnabled || animator == null) return;
            Apply(actualSpeed, requestedRun, state, climbing);
        }

        private void Apply(float actualSpeed, bool requestedRun, ThiefStateId state = ThiefStateId.Idle, bool climbing = false)
        {
            if (animator == null) return;
            if (cachedAnimator != animator || cachedController != animator.runtimeAnimatorController)
                RefreshParameters();
            if (cachedController == null) return;
            bool moving = actualSpeed > Mathf.Max(0.001f, movingThreshold);
            int action = state == ThiefStateId.StealMango ? 1
                : state == ThiefStateId.Hide && !moving ? 2
                : state == ThiefStateId.ClimbTree && climbing ? 3
                : state == ThiefStateId.TreeHide ? 4
                : state == ThiefStateId.ClimbDown && climbing ? 5 : 0;
            if (hasWalk) animator.SetBool(Walk, moving && !requestedRun && action == 0);
            if (hasRun) animator.SetBool(Run, moving && requestedRun && action == 0);
            if (hasSpeed) animator.SetFloat(Speed, actualSpeed);
            if (hasAction) animator.SetInteger(Action, action);
            if (hasState) animator.SetInteger(State, (int)state);
        }

        private void OnDisable() => Apply(0f, false);
    }
}
