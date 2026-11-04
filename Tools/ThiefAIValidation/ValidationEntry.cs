using System;
using MangoThiefAI;
using MangoThiefAI.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ValidationEntry
{
    public static void Run()
    {
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AnimatorController controller = ThiefAISetup.GetOrCreateController();
            if (controller.parameters.Length != 5 || controller.layers[0].stateMachine.states.Length != 8)
                throw new Exception("Controller shape invalid");
            if (ThiefAISetup.GetOrCreateController() != controller) throw new Exception("Controller was overwritten");
            var root = new GameObject("Editor setup test");
            var model = new GameObject("Model", typeof(Animator));
            model.transform.SetParent(root.transform, false);
            Selection.activeGameObject = root;
            ThiefAISetup.SetupSelectedThief();
            if (root.GetComponent<ThiefAI>() == null || root.GetComponent<GardenPlayer>() == null
                || root.GetComponent<ThiefMotor>() == null || root.GetComponent<CharacterController>() == null
                || model.GetComponent<Animator>().runtimeAnimatorController != controller)
                throw new Exception("Editor setup component wiring failed");
            Debug.Log("THIEF_VALIDATION PASS editor setup and controller generation/idempotence");
            UnityEngine.Object.DestroyImmediate(root);
            new GameObject("Validation Runner").AddComponent<FullThiefValidationRunner>();
            EditorApplication.isPlaying = true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }
}
