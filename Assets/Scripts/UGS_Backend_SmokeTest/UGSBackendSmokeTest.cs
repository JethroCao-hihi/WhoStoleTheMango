using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using Unity.Services.Core;
using UnityEngine;

/// <summary>
/// Temporary diagnostic panel for checking UGS Authentication + Cloud Save.
/// Keep this separate from the project's existing AuthManager/SaveManager.
/// Remove this component after the smoke test is complete.
/// </summary>
public class UGSBackendSmokeTest : MonoBehaviour
{
    private const string SaveKey = "ugs_backend_smoke_test_v1";

    [Header("Test account only - do not use a real password")]
    [SerializeField] private string username = "MinhTest01";
    [SerializeField] private string password = "TestGame!2026";
    [SerializeField] private int testExp = 1;

    private string status = "Ready. Click 1) Initialize UGS first.";
    private bool busy;
    private GUIStyle titleStyle;

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(20, 20, 560, 650), GUI.skin.box);
        GUILayout.Label("UGS BACKEND SMOKE TEST", MakeTitleStyle());
        GUILayout.Label("Temporary test panel. It does not replace AuthManager or SaveManager.");
        GUILayout.Space(8);

        GUILayout.Label("Test username (3-20 chars, letters/numbers and . - @ _):");
        username = GUILayout.TextField(username, 20);
        GUILayout.Label("Test password (8-30 chars; upper, lower, number, symbol):");
        password = GUILayout.PasswordField(password, '*', 30);
        GUILayout.Label("Test EXP value:");
        int.TryParse(GUILayout.TextField(testExp.ToString(), 10), out testExp);

        GUI.enabled = !busy;
        if (GUILayout.Button("1) Initialize Unity Services"))
            StartOperation(InitializeAsync);

        if (GUILayout.Button("2) Register new test account"))
            StartOperation(RegisterAsync);

        if (GUILayout.Button("3) Sign in existing test account"))
            StartOperation(SignInAsync);

        if (GUILayout.Button("4) Save test value to Cloud Save"))
            StartOperation(SaveAsync);

        if (GUILayout.Button("5) Load test value from Cloud Save"))
            StartOperation(LoadAsync);

        if (GUILayout.Button("Sign out"))
            SignOutNow();
        GUI.enabled = true;

        GUILayout.Space(10);
        GUILayout.Label("Status:");
        GUILayout.TextArea(status, GUILayout.MinHeight(110));

        if (UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn)
        {
            GUILayout.Space(5);
            GUILayout.Label("Signed-in Player ID:");
            GUILayout.TextArea(AuthenticationService.Instance.PlayerId ?? "(not available)", GUILayout.Height(40));
        }

        GUILayout.EndArea();
    }

    private GUIStyle MakeTitleStyle()
    {
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
        }
        return titleStyle;
    }

    private void StartOperation(Func<Task> operation)
    {
        // Start from the GUI event, but do not await inside OnGUI.
        _ = RunOperation(operation);
    }

    private async Task RunOperation(Func<Task> operation)
    {
        if (busy) return;
        busy = true;
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            status = $"FAILED: {ex.GetType().Name}\n{ex.Message}\nSee Unity Console for details.";
            Debug.LogException(ex);
        }
        finally
        {
            busy = false;
        }
    }

    private async Task InitializeAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();

        status = "Unity Services initialized. Next, register a new test account or sign in.";
        Debug.Log("[UGS Test] Unity Services initialized.");
    }

    private async Task RegisterAsync()
    {
        await EnsureInitializedAsync();
        if (AuthenticationService.Instance.IsSignedIn)
            throw new InvalidOperationException("You are already signed in. Click Sign out, then register a new test account.");

        ValidateInput();
        await AuthenticationService.Instance.SignUpWithUsernamePasswordAsync(username.Trim(), password);
        status = $"REGISTER OK\nPlayer ID: {AuthenticationService.Instance.PlayerId}\nRegistration also signs in the new account. Now try Save, Sign out, Sign in, then Load.";
        Debug.Log("[UGS Test] Registration succeeded. Player ID=" + AuthenticationService.Instance.PlayerId);
    }

    private async Task SignInAsync()
    {
        await EnsureInitializedAsync();
        if (AuthenticationService.Instance.IsSignedIn)
            throw new InvalidOperationException("Already signed in. Click Sign out before testing a fresh sign-in.");

        ValidateInput();
        await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(username.Trim(), password);
        status = $"SIGN-IN OK\nPlayer ID: {AuthenticationService.Instance.PlayerId}\nNow use Load to retrieve the cloud value.";
        Debug.Log("[UGS Test] Sign-in succeeded. Player ID=" + AuthenticationService.Instance.PlayerId);
    }

    private async Task SaveAsync()
    {
        await EnsureSignedInAsync();
        string payload = $"PlayerId={AuthenticationService.Instance.PlayerId}; TestEXP={testExp}; SavedUTC={DateTime.UtcNow:O}";
        var data = new Dictionary<string, object> { { SaveKey, payload } };
        await CloudSaveService.Instance.Data.Player.SaveAsync(data);
        status = $"CLOUD SAVE OK\nKey: {SaveKey}\nValue: {payload}\nNext: click Sign out, sign in again, then Load.";
        Debug.Log("[UGS Test] Cloud Save succeeded: " + payload);
    }

    private async Task LoadAsync()
    {
        await EnsureSignedInAsync();
        var result = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { SaveKey });
        if (!result.TryGetValue(SaveKey, out var item))
        {
            status = $"No value found for key '{SaveKey}'. This account has not successfully saved the test value yet.";
            Debug.LogWarning("[UGS Test] No Cloud Save value found for key " + SaveKey);
            return;
        }

        string payload = item.Value.GetAs<string>();
        status = $"CLOUD LOAD OK\nKey: {SaveKey}\nValue: {payload}";
        Debug.Log("[UGS Test] Cloud Save loaded: " + payload);
    }

    private async Task EnsureInitializedAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();
    }

    private async Task EnsureSignedInAsync()
    {
        await EnsureInitializedAsync();
        if (!AuthenticationService.Instance.IsSignedIn)
            throw new InvalidOperationException("No signed-in account. Register or sign in first.");
    }

    private void ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username cannot be empty.");
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be empty.");
    }

    private void SignOutNow()
    {
        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                status = "Services are not initialized yet. Click Initialize first.";
                return;
            }

            if (AuthenticationService.Instance.IsSignedIn)
                AuthenticationService.Instance.SignOut();

            status = "Signed out locally from the UGS session. The cloud account and its saved data are not deleted.";
            Debug.Log("[UGS Test] Signed out.");
        }
        catch (Exception ex)
        {
            status = $"SIGN OUT FAILED: {ex.GetType().Name}\n{ex.Message}";
            Debug.LogException(ex);
        }
    }
}
