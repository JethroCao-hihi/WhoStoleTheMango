using System.Collections.Generic;
using UnityEngine;

namespace MangoThiefAI
{
    /// <summary>Demo-only orchard construction and connection of imported GardenOwner roots.</summary>
    public sealed class ThiefDemoEnvironment : MonoBehaviour
    {
        public AStarGrid navigationGrid;
        public Camera demoCamera;
        public GardenTrap trapPrefab;
        public GardenDog dogPrefab;
        public SlipperProjectile slipperPrefab;
        public bool autoConnectOwners = true;
        public bool buildOrchardOnStart = true;
        public Transform orchardRoot;
        public Material barkMaterial;
        public Material leafMaterial;
        public Material fruitMaterial;
        public Material coverMaterial;
        public int ConnectedOwners { get; private set; }
        private readonly HashSet<gardenOwner> connected = new HashSet<gardenOwner>();
        private float nextScan;

        private void Start()
        {
            if (buildOrchardOnStart) BuildOrchard();
            ConnectOwners();
        }
        private void Update()
        {
            if (!autoConnectOwners || Time.time < nextScan) return;
            nextScan = Time.time + 1f;
            ConnectOwners();
        }
        public void ConnectOwners()
        {
            if (!autoConnectOwners || navigationGrid == null) return;
            foreach (GardenPlayer player in GardenPlayer.ActivePlayers)
            {
                if (player == null || player.gameObject.scene != gameObject.scene) continue;
                gardenOwner owner = player.GetComponent<gardenOwner>();
                if (owner == null || !owner.isActiveAndEnabled || !connected.Add(owner)) continue;
                owner.navigationGrid = navigationGrid;
                owner.groundMask = navigationGrid.groundMask;
                if (owner.movementCamera == null || owner.movementCamera.gameObject.scene != gameObject.scene)
                    owner.movementCamera = demoCamera != null ? demoCamera.transform : null;
                if (owner.trapPrefab == null) owner.trapPrefab = trapPrefab;
                if (owner.dogPrefab == null) owner.dogPrefab = dogPrefab;
                if (owner.slipperPrefab == null) owner.slipperPrefab = slipperPrefab;
                Debug.Log("[Thief demo] Connected " + owner.name + " to the demo grid and missing skill prefabs.", owner);
            }
            ConnectedOwners = 0;
            foreach (gardenOwner owner in connected) if (owner != null && owner.isActiveAndEnabled) ConnectedOwners++;
        }

        public void BuildOrchard()
        {
            if (orchardRoot != null) return;
            int obstacleLayer = LayerMask.NameToLayer("GardenObstacle");
            if (navigationGrid == null || obstacleLayer < 0) return;
            barkMaterial = Fallback(barkMaterial, new Color(0.35f, 0.17f, 0.06f));
            leafMaterial = Fallback(leafMaterial, new Color(0.12f, 0.42f, 0.16f));
            fruitMaterial = Fallback(fruitMaterial, new Color(1f, 0.75f, 0.08f));
            coverMaterial = Fallback(coverMaterial, new Color(0.22f, 0.33f, 0.19f));
            orchardRoot = new GameObject("Demo Orchard - mangoes, cover and climb anchors").transform;
            orchardRoot.SetParent(transform, false);
            foreach (Vector3 position in new[] { new Vector3(-8f, 0f, 6f), new Vector3(8f, 0f, 6f),
                new Vector3(-8f, 0f, -7f), new Vector3(8f, 0f, -7f) })
                CreateTree(position, obstacleLayer);
            foreach (Vector3 position in new[] { new Vector3(-5f, 0f, 3f), new Vector3(5f, 0f, -3f) })
            {
                Transform cover = new GameObject("Cover - hide on the occluded side").transform;
                cover.SetParent(orchardRoot, false);
                cover.position = position;
                Primitive("Solid cover", PrimitiveType.Cube, cover, Vector3.up,
                    new Vector3(2f, 2f, 2f), coverMaterial, obstacleLayer, true);
                foreach (Vector3 side in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                {
                    Transform spot = Anchor("Hide point", cover, side * 2.2f);
                    spot.gameObject.AddComponent<ThiefHideSpot>();
                }
            }
            navigationGrid.Rebuild();
        }
        private void CreateTree(Vector3 position, int obstacleLayer)
        {
            Transform root = new GameObject("Mango tree - climbable").transform;
            root.SetParent(orchardRoot, false);
            root.position = position;
            Primitive("Trunk", PrimitiveType.Cylinder, root, Vector3.up * 1.4f,
                new Vector3(0.7f, 1.4f, 0.7f), barkMaterial, obstacleLayer, true);
            Primitive("Canopy - visual only", PrimitiveType.Sphere, root, Vector3.up * 3.2f,
                new Vector3(3.4f, 2f, 3.4f), leafMaterial, 0, false);
            MangoTree mango = root.gameObject.AddComponent<MangoTree>();
            mango.harvestPoint = Anchor("Harvest point", root, Vector3.left * 1.6f);
            mango.fruitVisuals = new GameObject[6];
            for (int i = 0; i < 6; i++)
            {
                float angle = i * Mathf.PI / 3f;
                mango.fruitVisuals[i] = Primitive("Mango " + (i + 1), PrimitiveType.Sphere, root,
                    new Vector3(Mathf.Cos(angle), 2.5f, Mathf.Sin(angle)), Vector3.one * 0.3f,
                    fruitMaterial, 0, false);
            }
            ClimbableTree climb = root.gameObject.AddComponent<ClimbableTree>();
            climb.climbStartPoint = Anchor("Climb start - clear of trunk", root, Vector3.right * 1.6f);
            climb.climbEndPoint = Anchor("Climb end", root, new Vector3(1.6f, 2.6f, 0f));
            climb.hidePoint = Anchor("Tree hide - above dog reach", root, new Vector3(1.6f, 3.6f, 0f));
        }
        private static Transform Anchor(string name, Transform parent, Vector3 local)
        {
            Transform point = new GameObject(name).transform;
            point.SetParent(parent, false);
            point.localPosition = local;
            return point;
        }
        private static Material Fallback(Material material, Color color)
        {
            return material != null ? material : new Material(Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard")) { color = color };
        }
        private static GameObject Primitive(string name, PrimitiveType type, Transform parent,
            Vector3 local, Vector3 scale, Material material, int layer, bool solid)
        {
            GameObject visual = GameObject.CreatePrimitive(type);
            visual.name = name;
            visual.transform.SetParent(parent, false);
            visual.transform.localPosition = local;
            visual.transform.localScale = scale;
            visual.layer = layer;
            visual.GetComponent<Renderer>().sharedMaterial = material;
            if (!solid)
            {
                Collider collider = visual.GetComponent<Collider>();
                collider.enabled = false;
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }
            return visual;
        }
    }
}
