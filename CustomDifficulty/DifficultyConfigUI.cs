using System.Collections.Generic;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DifficultyConfigUI
{
    private static bool _show = false;

    private static bool Show
    {
        get { return _show; }
        set
        {
            // Check if the menu is actively being opened
            if (_show == false && value == true)
            {
                // Only the host needs to reload from storage
                if (IsHost())
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

    private static readonly Dictionary<string, string> textBuffers = [];

    public static void OnGUI()
    {
        if (!Show) return;

        // Change the title based on who is looking at it
        string windowTitle = IsHost() ? "Custom Difficulty Settings" : "Custom Difficulty Settings (Host - Read Only)";

        Rect windowRect = new(Screen.width / 2 - 200, Screen.height / 2 - 250, 400, 500);
        windowRect = GUI.Window(857312, windowRect, DrawConfigWindow, windowTitle);
    }

    public static void Update()
    {
        if (!Show) return;

        // If we are no longer in the lobby, close the menu and stop checking
        if (SceneManager.GetActiveScene().name != "lobby") Show = false;
    }

    public static void RuleChoiceController_OnClick(On.RoR2.UI.RuleChoiceController.orig_OnClick orig, RuleChoiceController self)
    {
        orig(self);

        NetworkUser networkUser = self.FindNetworkUser();
        if (!networkUser)
        {
            return;
        }
        PreGameRuleVoteController preGameRuleVoteController = PreGameRuleVoteController.FindForUser(networkUser);
        if (!preGameRuleVoteController)
        {
            return;
        }

        Show = self.choiceDef.difficultyIndex == CustomDifficulty.DifficultyIndex && preGameRuleVoteController.IsChoiceVoted(self.choiceDef);
    }

    private static void DrawConfigWindow(int windowID)
    {
        GUILayout.BeginArea(new Rect(10, 25, 380, 465));

        // Difficulty scaling factor
        GUILayout.BeginHorizontal();
        GUILayout.Label("Scaling Factor (Time Diff.)", GUILayout.Width(200));

        // Determine value to show
        float currentScaling = IsHost() ? DifficultyConfig.Configs.ScalingFactor!.Value : DifficultyConfig.ActiveStats.ScalingFactor;

        if (!textBuffers.ContainsKey("ScalingFactor") || !IsHost())
        {
            textBuffers["ScalingFactor"] = currentScaling.ToString();
        }

        // Assign a unique control name BEFORE drawing the TextField
        GUI.SetNextControlName("ScalingFactor");
        GUI.enabled = IsHost(); // Read-only lock for clients
        textBuffers["ScalingFactor"] = GUILayout.TextField(textBuffers["ScalingFactor"], GUILayout.Width(100));
        GUI.enabled = true;

        if (IsHost())
        {
            // Check if the user is currently typing in this specific box
            bool isFocused = GUI.GetNameOfFocusedControl() == "ScalingFactor";

            if (float.TryParse(textBuffers["ScalingFactor"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedScaling))
            {
                DifficultyConfig.Configs.ScalingFactor!.Value = parsedScaling;
            }
            else if (!isFocused)
            {
                // Revert to default (1.0) when unfocused and blank/invalid
                DifficultyConfig.Configs.ScalingFactor!.Value = 1f;
                textBuffers["ScalingFactor"] = "1";
            }
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
        var configKeys = selectedTab == 0 ? DifficultyConfig.Configs.Player : DifficultyConfig.Configs.Enemy;

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

            float displayValue = (float)DifficultyConfig.Stat.TypeFromString(statName);
            if (IsHost())
            {
                displayValue = kvp.Value.Value;
            }
            else
            {
                if (syncedStats.TryGetValue(statName, out DifficultyConfig.Stat displayStat))
                {
                    displayValue = displayStat.Value;
                }
            }

            if (!textBuffers.TryGetValue(bufferId, out string existingBuffer))
            {
                textBuffers[bufferId] = displayValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (!IsHost())
            {
                if (float.TryParse(existingBuffer, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedBuffer) && parsedBuffer != displayValue)
                {
                    textBuffers[bufferId] = displayValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            GUILayout.BeginHorizontal();
            Color originalColor = GUI.contentColor;

            // Dynamically calculate the default value for this specific stat
            float defaultValue = (float)DifficultyConfig.Stat.TypeFromString(statName);

            // Compare the current display value against the actual default
            if (displayValue != defaultValue)
            {
                GUI.contentColor = Color.yellow;
            }

            GUILayout.Label(statName, GUILayout.Width(200));

            // Assign the unique bufferId as the control name
            GUI.SetNextControlName(bufferId);
            GUI.enabled = IsHost();
            textBuffers[bufferId] = GUILayout.TextField(textBuffers[bufferId], GUILayout.Width(100));
            GUI.enabled = true;

            if (IsHost())
            {
                // Check if the user is currently typing in this specific box
                bool isFocused = GUI.GetNameOfFocusedControl() == bufferId;

                if (float.TryParse(textBuffers[bufferId], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedValue))
                {
                    kvp.Value.Value = parsedValue;
                }
                else if (!isFocused)
                {
                    // Revert to the stat's default value when unfocused and blank/invalid
                    kvp.Value.Value = defaultValue;
                    textBuffers[bufferId] = defaultValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            GUI.contentColor = originalColor;
            GUILayout.EndHorizontal();
        }

        GUILayout.EndScrollView();
        GUILayout.Space(15);

        if (GUILayout.Button(IsHost() ? "Save & Close" : "Close", GUILayout.Height(30)))
        {
            Show = false;
        }

        GUILayout.EndArea();
    }

    private static bool IsHost()
    {
        return UnityEngine.Networking.NetworkServer.active;
    }
}
