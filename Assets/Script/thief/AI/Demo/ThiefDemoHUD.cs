using UnityEngine;

namespace MangoThiefAI
{
    /// <summary>Optional controls and telemetry, attached only in the demonstration scene.</summary>
    public sealed class ThiefDemoHUD : MonoBehaviour
    {
        public ThiefAI[] thieves = new ThiefAI[0];
        public ThiefDemoEnvironment environment;

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16f, 16f, Mathf.Min(520f, Screen.width - 32f), 430f), GUI.skin.box);
            GUILayout.Label("MANGO THIEF - FULL GAMEPLAY DEMO");
            GUILayout.Label("Add a GardenOwner root to this scene. WASD / Shift / Q / E / R.");
            GUILayout.Label("Owners connected: " + (environment != null ? environment.ConnectedOwners : 0));
            GUILayout.Label("Thieves harvest, hide, flee and climb trees. Select + Gizmos for paths.");
            if (thieves != null)
                foreach (ThiefAI ai in thieves)
                {
                    if (ai == null || ai.Motor == null || ai.Player == null) continue;
                    GUILayout.Space(12f);
                    GUILayout.Label(ai.name + " | " + ai.CurrentState
                        + (ai.Player.IsRooted ? " | ROOTED" : ""));
                    GUILayout.Label("Speed: " + ai.Motor.ActualSpeed.ToString("F1") + " units/s");
                    GUILayout.Label("Mangoes: " + ai.Inventory.MangoCount + " | HP: " + ai.Player.Health.ToString("F0")
                        + " | Danger: " + ai.Perception.Danger);
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Root for 2 seconds")) ai.Player.Root(2f);
                    if (GUILayout.Button("Next leg: " + (ai.runBetweenWaypoints ? "Run" : "Walk")))
                        ai.runBetweenWaypoints = !ai.runBetweenWaypoints;
                    GUILayout.EndHorizontal();
                    if (GUILayout.Button(ai.followTestWaypoints ? "Switch to mango mission" : "Switch to waypoint test"))
                    {
                        ai.followTestWaypoints = !ai.followTestWaypoints;
                        if (!ai.Climber.ControlsMovement) ai.ReturnToIdle();
                    }
                    if (!string.IsNullOrEmpty(ai.LastMoveFailure)) GUILayout.Label(ai.LastMoveFailure);
                }
            GUILayout.EndArea();
        }
    }
}
