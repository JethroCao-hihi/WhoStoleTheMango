using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MangoThiefAI.Editor
{
    public static class ThiefAIDemo
    {
        private const string Output = "Assets/ThiefAIGenerated";

        [MenuItem("Tools/Thief AI/Create Demo Scene")]
        public static void CreateDemoScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                {
                    Debug.LogWarning("Save the untitled scene before creating the Thief AI demo.");
                    return;
                }
            int groundLayer = LayerMask.NameToLayer("GardenGround");
            int obstacleLayer = LayerMask.NameToLayer("GardenObstacle");
            if (groundLayer < 0 || obstacleLayer < 0)
            {
                Debug.LogError("Create the GardenGround and GardenObstacle layers before creating the demo.");
                return;
            }

            ThiefAISetup.GetOrCreateController();
            Material groundMaterial = DemoMaterial("DemoGround", new Color(0.25f, 0.48f, 0.24f));
            Material wallMaterial = DemoMaterial("DemoWall", new Color(0.35f, 0.4f, 0.45f));
            Material walkMaterial = DemoMaterial("DemoWalk", new Color(0.15f, 0.5f, 0.95f));
            Material runMaterial = DemoMaterial("DemoRun", new Color(1f, 0.55f, 0.15f));
            Material bark = DemoMaterial("DemoBark", new Color(0.35f, 0.17f, 0.06f));
            Material leaves = DemoMaterial("DemoLeaves", new Color(0.12f, 0.42f, 0.16f));
            Material mangoes = DemoMaterial("DemoMangoes", new Color(1f, 0.75f, 0.08f));
            Material cover = DemoMaterial("DemoCover", new Color(0.22f, 0.33f, 0.19f));

            // Preserve all currently loaded scenes and their unsaved changes.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            GameObject ground = Cube("Ground", new Vector3(0f, -0.25f, 0f),
                new Vector3(30f, 0.5f, 30f), groundMaterial);
            ground.layer = groundLayer;
            GameObject wall = Cube("Wall - thieves must go around", new Vector3(0f, 1f, 0f),
                new Vector3(1f, 2f, 8f), wallMaterial);
            wall.layer = obstacleLayer;

            AStarGrid grid = new GameObject("A Star Grid").AddComponent<AStarGrid>();
            grid.worldSize = new Vector2(30f, 30f);
            grid.groundMask = 1 << groundLayer;
            grid.obstacleMask = 1 << obstacleLayer;
            grid.agentHeight = 1.8f;
            grid.agentRadius = 0.3f;
            grid.maxStepHeight = 0.3f;

            Transform waypoints = new GameObject("Demo Waypoints").transform;
            Transform walkLeft = Waypoint("Walk - left", new Vector3(-6f, 0f, -2f), waypoints, walkMaterial);
            Transform walkRight = Waypoint("Walk - right", new Vector3(6f, 0f, -2f), waypoints, walkMaterial);
            Transform runRight = Waypoint("Run - right", new Vector3(6f, 0f, 2f), waypoints, runMaterial);
            Transform runLeft = Waypoint("Run - left", new Vector3(-6f, 0f, 2f), waypoints, runMaterial);
            ThiefAI walker = CreateThief("Thief - Walk (blue)", walkLeft.position + Vector3.up * 0.05f,
                grid, new[] { walkRight, walkLeft }, false, walkMaterial);
            ThiefAI runner = CreateThief("Thief - Run (orange)", runRight.position + Vector3.up * 0.05f,
                grid, new[] { runLeft, runRight }, true, runMaterial);
            ThiefDemoHUD hud = new GameObject("Demo Controls").AddComponent<ThiefDemoHUD>();
            hud.thieves = new[] { walker, runner };

            Camera camera = new GameObject("Demo Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0f, 19f, -19f);
            camera.transform.LookAt(Vector3.zero);
            ThiefDemoEnvironment environment = hud.gameObject.AddComponent<ThiefDemoEnvironment>();
            environment.navigationGrid = grid;
            environment.demoCamera = camera;
            environment.trapPrefab = AssetDatabase.LoadAssetAtPath<GardenTrap>("Assets/GardenOwnerGenerated/Trap.prefab");
            environment.dogPrefab = AssetDatabase.LoadAssetAtPath<GardenDog>("Assets/GardenOwnerGenerated/Dog.prefab");
            environment.slipperPrefab = AssetDatabase.LoadAssetAtPath<SlipperProjectile>("Assets/GardenOwnerGenerated/Slipper.prefab");
            environment.barkMaterial = bark;
            environment.leafMaterial = leaves;
            environment.fruitMaterial = mangoes;
            environment.coverMaterial = cover;
            environment.BuildOrchard();
            hud.environment = environment;
            Light sun = new GameObject("Sun", typeof(Light)).GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.3f;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            string path = AssetDatabase.GenerateUniqueAssetPath(Output + "/ThiefDemo.unity");
            EditorSceneManager.SaveScene(scene, path);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = walker.gameObject;
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(path));
            Debug.Log("Thief demo saved: " + path + ". Open this scene by itself and press Play. "
                + "Thieves harvest mangoes, hide/flee from an imported GardenOwner and climb away from dogs. "
                + "Add your GardenOwner root to this scene; the demo connects its grid and missing skill prefabs. "
                + "Capsule models are placeholders; assign your animation clips separately.");
        }

        private static ThiefAI CreateThief(string name, Vector3 position, AStarGrid grid,
            Transform[] targets, bool run, Material material)
        {
            GameObject root = new GameObject(name);
            root.transform.position = position;
            CharacterController controller = root.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.3f;
            controller.center = Vector3.up * 0.9f;
            controller.stepOffset = 0.3f;
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Placeholder - replace with character model";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = Vector3.up * 0.9f;
            visual.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.GetComponent<Renderer>().sharedMaterial = material;
            Animator animator = visual.AddComponent<Animator>();
            animator.runtimeAnimatorController = ThiefAISetup.GetOrCreateController();
            animator.applyRootMotion = false;

            ThiefAI ai = root.AddComponent<ThiefAI>();
            root.GetComponent<ThiefMotor>().navigationGrid = grid;
            root.GetComponent<ThiefAnimationDriver>().animator = animator;
            ai.followTestWaypoints = false;
            ai.enableMangoMission = true;
            ai.testWaypoints = targets;
            ai.runBetweenWaypoints = run;
            ai.idleDuration = 1f;
            return ai;
        }

        private static Transform Waypoint(string name, Vector3 position, Transform parent, Material material)
        {
            Transform point = new GameObject(name).transform;
            point.SetParent(parent, false);
            point.position = position;
            GameObject marker = Cube("Waypoint marker - no collider", Vector3.zero,
                new Vector3(1f, 0.06f, 1f), material);
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.transform.SetParent(point, false);
            marker.transform.localPosition = Vector3.up * 0.03f;
            return point;
        }

        private static GameObject Cube(string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.position = position;
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            return cube;
        }

        private static Material DemoMaterial(string name, Color color)
        {
            string path = Output + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
