using BepInEx;
using R2API;
using R2API.Networking;
using R2API.Utils;
using RoR2;
using System.Collections.Generic;
using UnityEngine.Networking;

[BepInDependency(DifficultyAPI.PluginGUID)]
[BepInDependency(LanguageAPI.PluginGUID)]
[BepInDependency(NetworkingAPI.PluginGUID)]
[BepInDependency(RecalculateStatsAPI.PluginGUID)]
[BepInPlugin(PluginGUID, PluginName, PluginVersion)]
[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.EveryoneNeedSameModVersion)]
public class CustomDifficulty : BaseUnityPlugin
{
    public const string PluginGUID = PluginAuthor + "." + PluginName;
    public const string PluginAuthor = "Cubesicle";
    public const string PluginName = "CustomDifficulty";
    public const string PluginVersion = "0.0.0";

    public static DifficultyDef? DifficultyDef { get; private set; }
    public static DifficultyIndex DifficultyIndex { get; private set; }

    private RecalculateStatsAPI.StatHookEventHandler? statHookEventHandler;

    private void Awake()
    {
        Log.Init(Logger);

        // Config
        DifficultyConfig.configFile = Config;
        DifficultyConfig.Bind();

        // Networking
        NetworkingAPI.RegisterMessageType<SyncConfigMessage>();

        // Hooks
        statHookEventHandler = new RecalculateStatsAPI.StatHookEventHandler(RecalculateStatsAPI_GetStatCoefficients);

        RoR2.NetworkUser.onPostNetworkUserStart += NetworkUser_onPostNetworkUserStart;
        On.RoR2.UI.RuleChoiceController.Start += DifficultyConfigUI.RuleChoiceController_Start;
        Run.onRunStartGlobal += Run_onRunStartGlobal;
        Run.onRunDestroyGlobal += Run_onRunDestroyGlobal;

        // Difficulty
        DifficultyDef = new DifficultyDef(
            0f, //This is the scaling factor, and decides how quickly the difficulty ramps up. drizzle is 1, rainstorm=2, monsoon=3.
            "Custom",//The name token. consider using AssetPlus.Language to add your tokens.
            "", //The iconpath, You can use a vanilla icon, or with use of AssetAPI/RescourceAPI use your own custom one.
            "idk",//The description token. consider using AssetPlus.Language to add your tokens.
            new UnityEngine.Color(0.5f, 0.1f, 0.2f),//The color that appears when hovering over this in the rulebook.
            "custom",
            false
        );
        DifficultyIndex = DifficultyAPI.AddDifficulty(DifficultyDef);
    }

    private void OnDestroy()
    {
        // Cleanup logic
        RoR2.NetworkUser.onPostNetworkUserStart -= NetworkUser_onPostNetworkUserStart;
        On.RoR2.UI.RuleChoiceController.Start -= DifficultyConfigUI.RuleChoiceController_Start;
        Run.onRunStartGlobal -= Run_onRunStartGlobal;
        Run.onRunDestroyGlobal -= Run_onRunDestroyGlobal;

        if (statHookEventHandler is not null)
        {
            RecalculateStatsAPI.GetStatCoefficients -= statHookEventHandler;
        }
    }

    private void OnGUI()
    {
        DifficultyConfigUI.OnGUI();
    }

    private void OnUpdate()
    {
        DifficultyConfigUI.Update();
    }

    private void NetworkUser_onPostNetworkUserStart(NetworkUser networkUser)
    {
        // If we are the server, and the person joining is a client, push the stats to them
        if (NetworkServer.active && !networkUser.isLocalPlayer)
        {
            DifficultyConfig.SyncHostConfigToClients();
        }
    }

    private void RecalculateStatsAPI_GetStatCoefficients(CharacterBody sender, RecalculateStatsAPI.StatHookEventArgs args)
    {
        if (!sender || !sender.teamComponent) return;

        Dictionary<string, float>? activeStats = null;

        if (sender.isPlayerControlled)
        {
            // Human players get the Player Stats
            activeStats = DifficultyConfig.ActivePlayerStats;
        }
        else if (sender.teamComponent.teamIndex != TeamIndex.Player)
        {
            // Entities not on the player team get the Enemy Stats
            activeStats = DifficultyConfig.ActiveEnemyStats;
        }
        else
        {
            // If it's on the Player team but NOT player-controlled, 
            // we do nothing and exit early.
            return;
        }

        // Iterate over the synced active stats directly
        foreach (var kvp in activeStats)
        {
            // If the stat value is 0, skip it to save processing time
            if (kvp.Value == 0f) continue;

            // Look up the pre-compiled delegate by the field's name
            if (DifficultyConfig.Accessors.TryGetValue(kvp.Key, out var accessor))
            {
                // Execute delegates
                float currentValue = accessor.Get(args);
                accessor.Set(args, currentValue + kvp.Value);
            }
        }
    }

    private void Run_onRunStartGlobal(Run run)
    {
        RecalculateStatsAPI.GetStatCoefficients -= statHookEventHandler; // Guard against duplicate hook
        if (run.selectedDifficulty == DifficultyIndex)
        {
            if (NetworkServer.active) DifficultyConfig.SyncHostConfigToClients();
            RecalculateStatsAPI.GetStatCoefficients += statHookEventHandler; // Hook
        }
    }

    private void Run_onRunDestroyGlobal(Run run)
    {
        if (run.selectedDifficulty == DifficultyIndex)
        {
            RecalculateStatsAPI.GetStatCoefficients -= statHookEventHandler; // Unhook
        }
    }
}
