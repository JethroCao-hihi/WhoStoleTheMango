using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MangoThiefAI.Editor
{
    public static class ThiefAISetup
    {
        public const string ControllerPath = "Assets/ThiefAIGenerated/ThiefLocomotion.controller";

        [MenuItem("Tools/Thief AI/Create Locomotion Controller")]
        public static void CreateLocomotionController()
        {
            AnimatorController controller = GetOrCreateController();
            Selection.activeObject = controller;
            EditorGUIUtility.PingObject(controller);
        }

        public static AnimatorController GetOrCreateController()
        {
            AnimatorController existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (existing != null)
            {
                EnsureGameplayAnimations(existing);
                return existing;
            }
            if (!AssetDatabase.IsValidFolder("Assets/ThiefAIGenerated"))
                AssetDatabase.CreateFolder("Assets", "ThiefAIGenerated");
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("isWalk", AnimatorControllerParameterType.Bool);
            controller.AddParameter("isRun", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState idle = machine.AddState("Idle", new Vector3(250f, 0f));
            AnimatorState walk = machine.AddState("Walk", new Vector3(250f, 80f));
            AnimatorState run = machine.AddState("Run", new Vector3(250f, 160f));
            machine.defaultState = idle;
            // Motions deliberately remain empty: assign clips compatible with the thief rig.
            AddTransition(machine, idle, false, false);
            AddTransition(machine, walk, true, false);
            AddTransition(machine, run, false, true);
            EnsureGameplayAnimations(controller);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static void EnsureGameplayAnimations(AnimatorController controller)
        {
            foreach (string parameter in new[] { "Action", "AIState" })
            {
                bool found = false;
                foreach (AnimatorControllerParameter existing in controller.parameters)
                    if (existing.name == parameter) { found = true; break; }
                if (!found) controller.AddParameter(parameter, AnimatorControllerParameterType.Int);
            }
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            foreach (AnimatorStateTransition transition in machine.anyStateTransitions)
            {
                if (transition.destinationState == null) continue;
                string name = transition.destinationState.name;
                if (name != "Idle" && name != "Walk" && name != "Run") continue;
                bool found = false;
                foreach (AnimatorCondition condition in transition.conditions)
                    if (condition.parameter == "Action") found = true;
                if (!found) transition.AddCondition(AnimatorConditionMode.Equals, 0f, "Action");
            }
            string[] names = { "StealMango", "Hide", "Climb", "TreeIdle", "ClimbDown" };
            for (int i = 0; i < names.Length; i++)
            {
                AnimatorState state = null;
                foreach (ChildAnimatorState child in machine.states)
                    if (child.state.name == names[i]) { state = child.state; break; }
                if (state == null) state = machine.AddState(names[i], new Vector3(520f, i * 80f));
                bool found = false;
                foreach (AnimatorStateTransition existing in machine.anyStateTransitions)
                    if (existing.destinationState == state) { found = true; break; }
                if (found) continue;
                AnimatorStateTransition transition = machine.AddAnyStateTransition(state);
                transition.hasExitTime = false;
                transition.canTransitionToSelf = false;
                transition.duration = 0.12f;
                transition.AddCondition(AnimatorConditionMode.Equals, i + 1, "Action");
            }
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        private static void AddTransition(AnimatorStateMachine machine, AnimatorState state, bool walk, bool run)
        {
            AnimatorStateTransition transition = machine.AddAnyStateTransition(state);
            transition.hasExitTime = false;
            transition.canTransitionToSelf = false;
            transition.duration = 0.12f;
            transition.AddCondition(walk ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, "isWalk");
            transition.AddCondition(run ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, "isRun");
        }

        [MenuItem("Tools/Thief AI/Setup Selected Thief")]
        public static void SetupSelectedThief()
        {
            GameObject selected = Selection.activeGameObject;
            if (Application.isPlaying || selected == null || EditorUtility.IsPersistent(selected))
            {
                Debug.LogWarning("Select a thief root in a scene, outside Play mode.");
                return;
            }
            if (selected.GetComponent<gardenOwner>() != null || selected.GetComponent<GardenDog>() != null)
            {
                Debug.LogError("Select a separate thief root without gardenOwner or GardenDog.", selected);
                return;
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Setup Thief AI");
            CharacterController controller = selected.GetComponent<CharacterController>();
            if (controller == null)
            {
                controller = Undo.AddComponent<CharacterController>(selected);
                controller.height = 1.8f;
                controller.radius = 0.3f;
                controller.center = Vector3.up * 0.9f;
                controller.stepOffset = 0.3f;
            }
            ThiefAI ai = selected.GetComponent<ThiefAI>();
            if (ai == null) ai = Undo.AddComponent<ThiefAI>(selected);
            if (selected.GetComponent<ThiefPerception>() == null) Undo.AddComponent<ThiefPerception>(selected);
            if (selected.GetComponent<ThiefClimber>() == null) Undo.AddComponent<ThiefClimber>(selected);
            if (selected.GetComponent<ThiefInventory>() == null) Undo.AddComponent<ThiefInventory>(selected);
            if (selected.GetComponent<ThiefTactics>() == null) Undo.AddComponent<ThiefTactics>(selected);
            ThiefMotor motor = selected.GetComponent<ThiefMotor>();
            Undo.RecordObject(motor, "Assign Thief Grid");
            // Only resolve an unambiguous grid in this scene, once in this editor command.
            if (motor.navigationGrid == null)
            {
                AStarGrid[] grids = Object.FindObjectsByType<AStarGrid>(FindObjectsSortMode.None);
                AStarGrid candidate = null;
                int count = 0;
                foreach (AStarGrid grid in grids)
                    if (grid.gameObject.scene == selected.scene) { candidate = grid; count++; }
                if (count == 1) motor.navigationGrid = candidate;
            }
            ThiefAnimationDriver driver = selected.GetComponent<ThiefAnimationDriver>();
            Undo.RecordObject(driver, "Assign Thief Animator");
            if (driver.animator == null) driver.animator = selected.GetComponentInChildren<Animator>(true);
            if (driver.animator != null)
            {
                Undo.RecordObject(driver.animator, "Configure Thief Animator");
                if (driver.animator.runtimeAnimatorController == null)
                    driver.animator.runtimeAnimatorController = GetOrCreateController();
                driver.animator.applyRootMotion = false;
                EditorUtility.SetDirty(driver.animator);
            }
            EditorUtility.SetDirty(motor);
            EditorUtility.SetDirty(driver);
            EditorUtility.SetDirty(ai);
            PrefabUtility.RecordPrefabInstancePropertyModifications(motor);
            PrefabUtility.RecordPrefabInstancePropertyModifications(driver);
            if (driver.animator != null) PrefabUtility.RecordPrefabInstancePropertyModifications(driver.animator);
            EditorSceneManager.MarkSceneDirty(selected.scene);
            Undo.CollapseUndoOperations(group);
            Debug.Log("Thief AI configured. Assign ThiefMotor.navigationGrid, match the grid agent size to the "
                + "CharacterController, and optionally enable test waypoints on ThiefAI. Assign Idle/Walk/Run clips.", selected);
        }
    }
}
