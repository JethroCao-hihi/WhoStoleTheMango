using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GardenOwnerSetup
{
    private const string Output = "Assets/GardenOwnerGenerated";

    [MenuItem("Tools/Garden Owner/Create Demo Scene")]
    public static void CreateDemoScene()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            if (!string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path)) continue;
            Debug.LogWarning("Save the current untitled scene before creating the Garden Owner demo.");
            return;
        }
        // Open additively so the user's current scene and unsaved edits stay intact.
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        int groundLayer = EnsureLayer("GardenGround");
        int obstacleLayer = EnsureLayer("GardenObstacle");
        EnsureAssets();

        GameObject ground = Cube("Ground", new Vector3(0f, -0.25f, 0f), new Vector3(30f, 0.5f, 30f));
        ground.layer = groundLayer;
        ground.GetComponent<Renderer>().sharedMaterial = Material("Ground", new Color(0.25f, 0.48f, 0.24f));
        GameObject wall = Cube("Wall - dog must go around", new Vector3(0f, 1f, 1f), new Vector3(7f, 2f, 1f));
        wall.layer = obstacleLayer;
        wall.GetComponent<Renderer>().sharedMaterial = Material("Wall", Color.gray);

        AStarGrid grid = new GameObject("A Star Grid").AddComponent<AStarGrid>();
        grid.worldSize = new Vector2(30f, 30f);
        grid.groundMask = 1 << groundLayer;
        grid.obstacleMask = 1 << obstacleLayer;
        grid.drawGrid = true;

        gardenOwner owner = CreatePlayer("Garden Owner", new Vector3(0f, 0.05f, -5f), true);
        Configure(owner, grid, groundLayer);
        CreatePlayer("Other Player - target", new Vector3(0f, 0.05f, 6f), false);

        Camera camera = new GameObject("Demo Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0f, 19f, -19f);
        camera.transform.LookAt(Vector3.zero);
        owner.movementCamera = camera.transform;
        Light light = new GameObject("Sun", typeof(Light)).GetComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.3f;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        grid.Rebuild();
        string scenePath = AssetDatabase.GenerateUniqueAssetPath(Output + "/GardenOwnerDemo.unity");
        EditorSceneManager.SaveScene(scene, scenePath);
        // Close other scenes in the editor only if the user chooses; avoid duplicate cameras at Play time.
        Selection.activeGameObject = owner.gameObject;
        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath));
        Debug.Log("Demo saved: " + scenePath + ". Open this scene by itself before Play. "
            + "WASD: walk, Shift: run, Space: jump, Q: trap, E: dog, R: slipper.");
    }

    [MenuItem("Tools/Garden Owner/Setup Selected Player")]
    public static void SetupSelectedPlayer()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null || EditorUtility.IsPersistent(selected))
        {
            Debug.LogWarning("Select the player root in the scene first.");
            return;
        }
        EnsureAssets();
        int groundLayer = EnsureLayer("GardenGround");
        int obstacleLayer = EnsureLayer("GardenObstacle");
        Undo.RegisterFullObjectHierarchyUndo(selected, "Setup Garden Owner");
        gardenOwner owner = selected.GetComponent<gardenOwner>();
        if (owner == null) owner = Undo.AddComponent<gardenOwner>(selected);
        CharacterController controller = selected.GetComponent<CharacterController>();
        controller.height = 1.8f;
        controller.radius = 0.3f;
        controller.center = Vector3.up * 0.9f;
        controller.stepOffset = 0.35f;
        AStarGrid grid = Object.FindFirstObjectByType<AStarGrid>();
        if (grid == null)
        {
            GameObject gridObject = new GameObject("A Star Grid");
            Undo.RegisterCreatedObjectUndo(gridObject, "Create A Star Grid");
            gridObject.transform.position = selected.transform.position;
            grid = gridObject.AddComponent<AStarGrid>();
            grid.groundMask = 1 << groundLayer;
            grid.obstacleMask = 1 << obstacleLayer;
        }
        Configure(owner, grid, groundLayer);
        Animator animator = selected.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            Undo.RecordObject(animator, "Assign Garden Owner Animator");
            animator.runtimeAnimatorController = CreateAnimator();
            animator.applyRootMotion = false;
            owner.animator = animator;
        }
        EditorUtility.SetDirty(owner);
        EditorSceneManager.MarkSceneDirty(selected.scene);
        Debug.Log("Garden Owner configured. Assign ground objects to GardenGround, walls/trees to "
            + "GardenObstacle, resize A Star Grid to cover the map, and add GardenPlayer to other players.");
    }

    private static void Configure(gardenOwner owner, AStarGrid grid, int groundLayer)
    {
        owner.navigationGrid = grid;
        owner.groundMask = 1 << groundLayer;
        owner.trapPrefab = AssetDatabase.LoadAssetAtPath<GardenTrap>(Output + "/Trap.prefab");
        owner.dogPrefab = AssetDatabase.LoadAssetAtPath<GardenDog>(Output + "/Dog.prefab");
        owner.slipperPrefab = AssetDatabase.LoadAssetAtPath<SlipperProjectile>(Output + "/Slipper.prefab");
    }

    private static gardenOwner CreatePlayer(string name, Vector3 position, bool controlled)
    {
        GameObject root = new GameObject(name);
        root.transform.position = position;
        CharacterController controller = root.AddComponent<CharacterController>();
        controller.height = 1.8f;
        controller.radius = 0.3f;
        controller.center = Vector3.up * 0.9f;
        controller.stepOffset = 0.35f;
        root.AddComponent<GardenPlayer>();
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        visual.name = "Replace with character model";
        visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = Vector3.up * 0.9f;
        visual.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
        Object.DestroyImmediate(visual.GetComponent<Collider>());
        visual.GetComponent<Renderer>().sharedMaterial = Material(controlled ? "Owner" : "Target",
            controlled ? new Color(0.95f, 0.68f, 0.15f) : new Color(0.15f, 0.5f, 0.95f));
        if (!controlled) return null;
        gardenOwner owner = root.AddComponent<gardenOwner>();
        // The placeholder has no rig; real character setup assigns the generated controller.
        return owner;
    }

    private static void EnsureAssets()
    {
        if (!AssetDatabase.IsValidFolder(Output)) AssetDatabase.CreateFolder("Assets", "GardenOwnerGenerated");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(Output + "/Trap.prefab") == null)
        {
            GameObject trap = new GameObject("Trap", typeof(GardenTrap));
            trap.GetComponent<SphereCollider>().radius = 0.65f;
            trap.GetComponent<SphereCollider>().isTrigger = true;
            trap.GetComponent<Rigidbody>().isKinematic = true;
            trap.GetComponent<Rigidbody>().useGravity = false;
            AddVisual(trap, PrimitiveType.Cylinder, new Vector3(1.1f, 0.06f, 1.1f), Vector3.up * 0.06f,
                Material("Trap", new Color(0.6f, 0.15f, 0.1f)));
            SavePrefab(trap, "Trap");
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(Output + "/Dog.prefab") == null)
        {
            GameObject dog = new GameObject("Dog", typeof(CharacterController), typeof(GardenDog));
            CharacterController controller = dog.GetComponent<CharacterController>();
            controller.height = 1f;
            controller.radius = 0.3f;
            controller.center = Vector3.up * 0.5f;
            controller.stepOffset = 0.35f;
            UnityEngine.Material brown = Material("Dog", new Color(0.38f, 0.2f, 0.1f));
            AddVisual(dog, PrimitiveType.Cube, new Vector3(0.45f, 0.4f, 0.8f), new Vector3(0f, 0.5f, 0f), brown);
            AddVisual(dog, PrimitiveType.Cube, new Vector3(0.35f, 0.35f, 0.4f), new Vector3(0f, 0.75f, 0.4f), brown);
            foreach (float x in new[] { -0.16f, 0.16f })
            foreach (float z in new[] { -0.25f, 0.25f })
                AddVisual(dog, PrimitiveType.Cube, new Vector3(0.1f, 0.3f, 0.1f), new Vector3(x, 0.2f, z), brown);
            SavePrefab(dog, "Dog");
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(Output + "/Slipper.prefab") == null)
        {
            GameObject slipper = new GameObject("Slipper", typeof(SlipperProjectile));
            slipper.GetComponent<BoxCollider>().size = new Vector3(0.2f, 0.08f, 0.4f);
            slipper.GetComponent<Rigidbody>().mass = 0.2f;
            AddVisual(slipper, PrimitiveType.Cube, new Vector3(0.2f, 0.08f, 0.4f), Vector3.zero,
                Material("Slipper", new Color(0.15f, 0.25f, 0.8f)));
            SavePrefab(slipper, "Slipper");
        }
        CreateAnimator();
        AssetDatabase.SaveAssets();
    }

    private static AnimatorController CreateAnimator()
    {
        string path = Output + "/GardenOwner.controller";
        AnimatorController existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (existing != null) return existing;
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        foreach (string parameter in new[] { "isJumping", "isWalk", "isRun" })
            controller.AddParameter(parameter, AnimatorControllerParameterType.Bool);
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState idle = machine.AddState("Idle");
        AnimatorState walk = machine.AddState("Walk");
        AnimatorState run = machine.AddState("Run");
        AnimatorState jump = machine.AddState("Jump");
        walk.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Sprite/mixamo.com 1.anim");
        run.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Sprite/mixamo.com.anim");
        machine.defaultState = idle;
        AddTransition(machine, jump, "isJumping");
        AddTransition(machine, walk, "isWalk");
        AddTransition(machine, run, "isRun");
        AnimatorStateTransition toIdle = machine.AddAnyStateTransition(idle);
        ConfigureTransition(toIdle);
        foreach (string parameter in new[] { "isJumping", "isWalk", "isRun" })
            toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, parameter);
        return controller;
    }

    private static void AddTransition(AnimatorStateMachine machine, AnimatorState state, string parameter)
    {
        AnimatorStateTransition transition = machine.AddAnyStateTransition(state);
        ConfigureTransition(transition);
        transition.AddCondition(AnimatorConditionMode.If, 0f, parameter);
    }

    private static void ConfigureTransition(AnimatorStateTransition transition)
    {
        transition.hasExitTime = false;
        transition.duration = 0.12f;
        transition.canTransitionToSelf = false;
    }

    private static int EnsureLayer(string name)
    {
        int existing = LayerMask.NameToLayer(name);
        if (existing >= 0) return existing;
        SerializedObject tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tags.FindProperty("layers");
        for (int i = 8; i < 32; i++)
        {
            SerializedProperty layer = layers.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(layer.stringValue)) continue;
            layer.stringValue = name;
            tags.ApplyModifiedProperties();
            return i;
        }
        throw new System.InvalidOperationException("No free user layer for " + name);
    }

    private static GameObject Cube(string name, Vector3 position, Vector3 scale)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.position = position;
        cube.transform.localScale = scale;
        return cube;
    }

    private static void AddVisual(GameObject root, PrimitiveType type, Vector3 scale, Vector3 offset,
        UnityEngine.Material material)
    {
        GameObject visual = GameObject.CreatePrimitive(type);
        visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = offset;
        visual.transform.localScale = scale;
        Object.DestroyImmediate(visual.GetComponent<Collider>());
        visual.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static UnityEngine.Material Material(string name, Color color)
    {
        string path = Output + "/" + name + ".mat";
        UnityEngine.Material material = AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(path);
        if (material != null) return material;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        material = new UnityEngine.Material(shader) { color = color };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void SavePrefab(GameObject root, string name)
    {
        PrefabUtility.SaveAsPrefabAsset(root, Output + "/" + name + ".prefab");
        Object.DestroyImmediate(root);
    }
}
