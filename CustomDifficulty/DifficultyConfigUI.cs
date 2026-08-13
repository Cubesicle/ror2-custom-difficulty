using RoR2;
using RoR2.UI;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DifficultyConfigUI
{
    private static bool _show = false;

    private static bool show
    {
        get { return _show; }
        set
        {
            // Check if the menu is actively being opened
            if (_show == false && value == true)
            {
                // Only the host needs to reload from storage
                if (isHost())
                {
                    DifficultyConfig.Reload();
                    textBuffers.Clear(); // Force the text boxes to refresh and read the new values
                }
            }

            // Check if the menu is actively being closed
            if (_show == true && value == false)
            {
                DifficultyConfig.Save();
            }

            _show = value;
        }
    }

    private static Vector2 scrollPosition = Vector2.zero;
    private static int selectedTab = 0; // 0 = Player, 1 = Enemy
    private static string searchQuery = "";

    private static Dictionary<string, string> textBuffers = new Dictionary<string, string>();

    public static void OnGUI()
    {
        if (!show) return;

        // Change the title based on who is looking at it
        string windowTitle = isHost() ? "Custom Difficulty Settings" : "Custom Difficulty Settings (Host - Read Only)";

        Rect windowRect = new Rect(Screen.width / 2 - 200, Screen.height / 2 - 250, 400, 500);
        windowRect = GUI.Window(857312, windowRect, DrawConfigWindow, windowTitle);
    }

    public static void Update()
    {
        if (!show) return;

        // If we are no longer in the lobby, close the menu and stop checking
        if (SceneManager.GetActiveScene().name != "lobby")
        {
            show = false;
            return;
        }
    }

    public static void RuleChoiceController_OnClick(On.RoR2.UI.RuleChoiceController.orig_OnClick orig, RuleChoiceController self)
    {
        orig(self);

        if (self.choiceDef is not null && PreGameController.instance && PreGameController.instance.readOnlyRuleBook != null)
        {
            DifficultyIndex clickedDifficulty = self.choiceDef.difficultyIndex;

            if (isHost())
            {
                // If the same difficulty is clicked on for the host, the difficulty changes, so hide the UI.
                show = (clickedDifficulty == CustomDifficulty.DifficultyIndex && PreGameController.instance.readOnlyRuleBook.FindDifficulty() != CustomDifficulty.DifficultyIndex);
            }
            else
            {
                show = (clickedDifficulty == CustomDifficulty.DifficultyIndex && PreGameController.instance.readOnlyRuleBook.FindDifficulty() == CustomDifficulty.DifficultyIndex);
            }
        }
    }

    private static void DrawConfigWindow(int windowID)
    {
        GUILayout.BeginArea(new Rect(10, 25, 380, 465));

        // Difficulty scaling factor
        GUILayout.BeginHorizontal();
        GUILayout.Label("Scaling Factor (Time Diff.)", GUILayout.Width(200));

        // Determine value to show
        float currentScaling = isHost() ? DifficultyConfig.ScalingFactorConfig!.Value : DifficultyConfig.ActiveStats.ScalingFactor;

        if (!textBuffers.ContainsKey("ScalingFactor") || !isHost())
        {
            textBuffers["ScalingFactor"] = currentScaling.ToString();
        }

        GUI.enabled = isHost(); // Read-only lock for clients
        textBuffers["ScalingFactor"] = GUILayout.TextField(textBuffers["ScalingFactor"], GUILayout.Width(100));
        GUI.enabled = true;

        if (isHost() && float.TryParse(textBuffers["ScalingFactor"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedScaling))
        {
            DifficultyConfig.ScalingFactorConfig!.Value = parsedScaling;
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(15);

        // Tabs
        GUILayout.BeginHorizontal();
        if (GUILayout.Toggle(selectedTab == 0, "Player Stats", "Button")) { if (selectedTab != 0) scrollPosition = Vector2.zero; selectedTab = 0; }
        if (GUILayout.Toggle(selectedTab == 1, "Enemy Stats", "Button")) { if (selectedTab != 1) scrollPosition = Vector2.zero; selectedTab = 1; }
        GUILayout.EndHorizontal();

        GUILayout.Space(5);

        // Search bar
        GUILayout.BeginHorizontal();
        GUILayout.Label("Search:", GUILayout.Width(50));
        searchQuery = GUILayout.TextField(searchQuery);

        if (GUILayout.Button("X", GUILayout.Width(25)))
        {
            searchQuery = "";
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(10);

        // Scroll view
        scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(300));

        // We still iterate over the Host's local config keys to get the list of stats
        var configKeys = selectedTab == 0 ? DifficultyConfig.PlayerConfigs : DifficultyConfig.EnemyConfigs;

        // But we need the Active dictionary to show clients the synced values
        var syncedStats = selectedTab == 0 ? DifficultyConfig.ActiveStats.Player : DifficultyConfig.ActiveStats.Enemy;

        foreach (var kvp in configKeys)
        {
            string statName = kvp.Key;

            if (!string.IsNullOrEmpty(searchQuery) && !statName.ToLower().Contains(searchQuery.ToLower()))
            {
                continue;
            }

            string bufferId = (selectedTab == 0 ? "P_" : "E_") + statName;

            float displayValue = 0f;
            if (isHost())
            {
                displayValue = kvp.Value.Value;
            }
            else
            {
                syncedStats.TryGetValue(statName, out displayValue);
            }

            if (!textBuffers.TryGetValue(bufferId, out string existingBuffer))
            {
                textBuffers[bufferId] = displayValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (!isHost())
            {
                if (float.TryParse(existingBuffer, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedBuffer) && parsedBuffer != displayValue)
                {
                    textBuffers[bufferId] = displayValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            GUILayout.BeginHorizontal();
            Color originalColor = GUI.contentColor;

            if (displayValue != 0f)
            {
                GUI.contentColor = Color.yellow;
            }

            GUILayout.Label(statName, GUILayout.Width(200));

            GUI.enabled = isHost();
            textBuffers[bufferId] = GUILayout.TextField(textBuffers[bufferId], GUILayout.Width(100));
            GUI.enabled = true;

            if (isHost())
            {
                if (float.TryParse(textBuffers[bufferId], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedValue))
                {
                    kvp.Value.Value = parsedValue;
                }
            }

            GUI.contentColor = originalColor;
            GUILayout.EndHorizontal();
        }

        GUILayout.EndScrollView();
        GUILayout.Space(15);

        if (GUILayout.Button(isHost() ? "Save & Close" : "Close", GUILayout.Height(30)))
        {
            show = false;
        }

        GUILayout.EndArea();
    }

    private static bool isHost()
    {
        return UnityEngine.Networking.NetworkServer.active;
    }
}
