using System.Collections;
using System;
using MangoThiefAI;
using UnityEditor;
using UnityEngine;

public sealed class FullThiefValidationRunner : MonoBehaviour
{
    private AStarGrid grid;
    private ThiefDemoEnvironment environment;
    private static int assertions;
    private static void Check(bool condition, string description)
    {
        if (condition) { assertions++; Debug.Log("THIEF_VALIDATION PASS " + description); return; }
        Debug.LogError("THIEF_VALIDATION FAIL " + description);
        EditorApplication.Exit(1);
        throw new Exception(description);
    }
    private static IEnumerator Until(Func<bool> condition, float timeout, string description)
    {
        float deadline = Time.realtimeSinceStartup + timeout;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Check(condition(), description);
    }
    private ThiefAI Actor(Vector3 position, bool mission = false)
    {
        var root = new GameObject("Test thief");
        root.transform.position = position + Vector3.up * 0.05f;
        var cc = root.AddComponent<CharacterController>();
        cc.height = 1.8f; cc.radius = 0.3f; cc.center = Vector3.up * 0.9f; cc.stepOffset = 0.3f;
        var ai = root.AddComponent<ThiefAI>();
        ai.Motor.navigationGrid = grid;
        ai.enableMangoMission = mission;
        ai.idleDuration = 0.1f;
        ai.harvestInterval = 0.2f;
        ai.safeWait = 0.5f;
        ai.minimumTreeTime = 0.3f;
        ai.minimumHideTime = 0.3f;
        ai.Perception.scanInterval = 0.05f;
        ai.Perception.dangerMemory = 0.6f;
        ai.Perception.dogMemory = 0.4f;
        ai.Perception.detectionDelay = 0.2f;
        return ai;
    }
    private gardenOwner Owner(Vector3 position, Vector3 forward)
    {
        var root = new GameObject("Test owner");
        root.transform.position = position + Vector3.up * 0.05f;
        root.transform.forward = forward;
        var cc = root.AddComponent<CharacterController>();
        cc.height = 1.8f; cc.radius = 0.3f; cc.center = Vector3.up * 0.9f;
        var owner = root.AddComponent<gardenOwner>();
        owner.readKeyboardInput = false;
        return owner;
    }
    private IEnumerator Start()
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.layer = 8;
        ground.transform.position = Vector3.down * 0.25f;
        ground.transform.localScale = new Vector3(30f, 0.5f, 30f);
        grid = new GameObject("Grid").AddComponent<AStarGrid>();
        grid.worldSize = new Vector2(30f, 30f);
        grid.groundMask = 1 << 8; grid.obstacleMask = 1 << 9;
        grid.agentHeight = 1.8f; grid.maxStepHeight = 0.3f;
        environment = new GameObject("Environment").AddComponent<ThiefDemoEnvironment>();
        environment.navigationGrid = grid;
        environment.BuildOrchard();
        var ai = Actor(new Vector3(-6f, 0f, -2f), true);
        yield return Until(() => ai.Inventory.MangoCount >= 2, 15f, "finds tree, follows A*, harvests and records mangoes");
        MangoTree harvested = ai.TargetMango;
        Check(harvested != null && harvested.MangoCount < harvested.startingMangoes, "harvest reduces shared tree stock");
        ai.Player.Root(0.6f);
        int fruit = ai.Inventory.MangoCount;
        yield return new WaitForSeconds(0.3f);
        Check(ai.Inventory.MangoCount == fruit, "root suspends harvesting");
        ai.Inventory.capacity = ai.Inventory.MangoCount;
        int stock = harvested.MangoCount;
        Check(!harvested.TryHarvest(ai.Inventory, ai.Motor.FeetPosition) && harvested.MangoCount == stock,
            "full inventory does not consume a mango");
        UnityEngine.Object.Destroy(ai.gameObject);
        yield return null;

        ai = Actor(new Vector3(-3f, 0f, 0f));
        var owner = Owner(new Vector3(-6f, 0f, 0f), Vector3.left);
        var cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cover.layer = 9; cover.transform.position = Vector3.up;
        cover.transform.localScale = new Vector3(1f, 2f, 3f);
        var hide = new GameObject("Test hide").AddComponent<ThiefHideSpot>();
        hide.transform.position = new Vector3(2f, 0f, 0f);
        grid.Rebuild();
        ai.transform.forward = Vector3.left;
        yield return Until(() => ai.CurrentState == ThiefStateId.Hide, 3f, "sensed owner facing away selects real occluded cover");
        Check(!ai.Perception.OwnerConfirmed, "nearby owner is distinct from confirmed detection");
        UnityEngine.Object.Destroy(owner.gameObject);
        yield return Until(() => ai.CurrentState == ThiefStateId.Idle, 12f, "hide waits out danger memory then returns to task");
        UnityEngine.Object.Destroy(ai.gameObject); UnityEngine.Object.Destroy(cover); UnityEngine.Object.Destroy(hide.gameObject);
        yield return null; grid.Rebuild();

        ai = Actor(new Vector3(-3f, 0f, -10f));
        ai.enabled = false;
        owner = Owner(new Vector3(-3f, 0f, -6f), Vector3.back);
        ai.transform.forward = Vector3.forward;
        float until = Time.realtimeSinceStartup + 0.6f;
        while (Time.realtimeSinceStartup < until) { ai.Perception.Tick(); yield return null; }
        Check(ai.Perception.OwnerConfirmed && ai.Perception.OwnerLookingAtMe, "LOS/FOV exposure confirms detection");
        float before = Vector3.Distance(ai.transform.position, owner.transform.position);
        ai.enabled = true;
        yield return Until(() => ai.CurrentState == ThiefStateId.Flee, 2f, "confirmed owner interrupts mission with flee");
        yield return new WaitForSeconds(0.35f);
        Check(Vector3.Distance(ai.transform.position, owner.transform.position) > before + 0.2f, "escape increases threat separation");
        UnityEngine.Object.Destroy(owner.gameObject);
        yield return Until(() => ai.CurrentState == ThiefStateId.Idle, 8f, "removed owner does not strand flee state");
        UnityEngine.Object.Destroy(ai.gameObject); yield return null;

        ai = Actor(new Vector3(-6f, 0f, -7f));
        ai.transform.forward = Vector3.left;
        owner = Owner(new Vector3(8f, 0f, -12f), Vector3.right);
        environment.ConnectOwners();
        Check(owner.navigationGrid == grid && owner.groundMask.value == grid.groundMask.value, "imported owner connects to demo navigation");
        var dogObject = new GameObject("Pursuing dog");
        dogObject.transform.position = new Vector3(-6f, 0.05f, -5f);
        var dogController = dogObject.AddComponent<CharacterController>();
        dogController.height = 1f; dogController.center = Vector3.up * 0.5f; dogController.radius = 0.3f;
        var dog = dogObject.AddComponent<GardenDog>();
        dog.damage = 0f; dog.moveSpeed = 0f; dog.lifetime = 20f;
        dog.Initialize(owner.GetComponent<GardenPlayer>(), grid);
        yield return Until(() => ai.Climber.IsTraversing, 5f, "pursuing dog selects reachable tree and starts continuous climb");
        Check(!ai.GetComponent<CharacterController>().enabled, "climb temporarily owns movement and disables controller");
        ai.Player.Root(0.5f);
        Vector3 pause = ai.transform.position;
        yield return new WaitForSeconds(0.2f);
        Check(Vector3.Distance(pause, ai.transform.position) < 0.01f, "root pauses climb without teleporting");
        yield return Until(() => ai.CurrentState == ThiefStateId.TreeHide, 6f, "climb completes at tree hide point");
        Check(ai.transform.position.y > 3f && Vector3.Distance(ai.transform.position, dog.transform.position) > dog.attackRange,
            "perch lies outside actual 3D dog attack range");
        Check(!ai.Climber.Tree.TryReserve(null), "tree rejects invalid reservation");
        ai.ReturnToIdle();
        Check(ai.CurrentState == ThiefStateId.TreeHide && ai.Climber.ControlsMovement,
            "ground cancel cannot strand a perched character in Idle");
        var blocker = new GameObject("Occupied landing");
        blocker.transform.position = ai.Climber.LandingPosition;
        var blockingController = blocker.AddComponent<CharacterController>();
        blockingController.height = 1.8f; blockingController.radius = 0.6f;
        blockingController.center = Vector3.up * 0.9f;
        UnityEngine.Object.Destroy(dog.gameObject);
        UnityEngine.Object.Destroy(owner.gameObject);
        yield return new WaitForSeconds(2f);
        Check(ai.Climber.IsPerched, "occupied landing postpones descent without crossing a character");
        UnityEngine.Object.Destroy(blocker);
        yield return Until(() => !ai.Climber.ControlsMovement, 10f, "after dog disappears AI waits safely then descends");
        Check(ai.GetComponent<CharacterController>().enabled && ai.transform.position.y < 0.2f, "descent restores collisions at ground");
        owner = Owner(new Vector3(8f, 0f, -12f), Vector3.right);
        dogObject = new GameObject("Second pursuing dog");
        dogObject.transform.position = ai.transform.position + Vector3.forward * 2f;
        dogController = dogObject.AddComponent<CharacterController>();
        dogController.height = 1f; dogController.center = Vector3.up * 0.5f; dogController.radius = 0.3f;
        dog = dogObject.AddComponent<GardenDog>();
        dog.damage = 0f; dog.moveSpeed = 0f; dog.lifetime = 20f;
        dog.Initialize(owner.GetComponent<GardenPlayer>(), grid);
        ai.transform.forward = Vector3.left;
        yield return Until(() => ai.Climber.IsPerched, 10f, "a fresh pursuit can reuse climbing after descent");
        UnityEngine.Object.Destroy(ai.Climber.Tree.gameObject);
        yield return Until(() => !ai.Climber.ControlsMovement, 8f, "destroyed tree triggers continuous cached descent even with a dog nearby");
        Check(ai.GetComponent<CharacterController>().enabled, "tree destruction restores controller");
        UnityEngine.Object.Destroy(dog.gameObject); UnityEngine.Object.Destroy(owner.gameObject);
        ai.Player.TakeDamage(1000f);
        yield return null;
        Check(ai.CurrentState == ThiefStateId.Idle && ai.Motor.Status == ThiefMoveStatus.None, "death cancels task and navigation");
        Debug.Log("THIEF_VALIDATION ALL PASSED: " + assertions + " assertions");
        EditorApplication.Exit(0);
    }
}
