using System.Collections.Generic;
using BepInEx;
using R2API;
using R2API.Networking;
using R2API.Utils;
using RoR2;

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
        Config.SaveOnConfigSet = false;
        DifficultyConfig.configFile = Config;
        DifficultyConfig.Bind();

        // Networking
        NetworkingAPI.RegisterMessageType<SyncConfigMessage>();

        // Hooks
        statHookEventHandler = new RecalculateStatsAPI.StatHookEventHandler(RecalculateStatsAPI_GetStatCoefficients);

        RoR2.NetworkUser.onPostNetworkUserStart += NetworkUser_onPostNetworkUserStart;
        On.RoR2.UI.RuleChoiceController.OnClick += DifficultyConfigUI.RuleChoiceController_OnClick;
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
        On.RoR2.UI.RuleChoiceController.OnClick -= DifficultyConfigUI.RuleChoiceController_OnClick;
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

    private void Update()
    {
        DifficultyConfigUI.Update();
    }

    private void NetworkUser_onPostNetworkUserStart(NetworkUser networkUser)
    {
        DifficultyConfig.Reload();
    }

    private void RecalculateStatsAPI_GetStatCoefficients(CharacterBody sender, RecalculateStatsAPI.StatHookEventArgs args)
    {
        if (!sender || !sender.teamComponent) return;

        Dictionary<string, DifficultyConfig.Stat>? activeStats = null;

        if (sender.isPlayerControlled)
        {
            // Human players get the Player Stats
            activeStats = DifficultyConfig.ActiveStats.Player;
        }
        else if (sender.teamComponent.teamIndex != TeamIndex.Player)
        {
            // Entities not on the player team get the Enemy Stats
            activeStats = DifficultyConfig.ActiveStats.Enemy;
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
            // If the stat value is the default value, skip it to save processing time
            if (kvp.Value.isDefault()) continue;

            // Look up the pre-compiled delegate by the field's name
            if (DifficultyConfig.Accessors.TryGetValue(kvp.Key, out var accessor))
            {
                // Execute delegates
                float currentValue = accessor.Get(args);
                accessor.Set(args, kvp.Value.Type == DifficultyConfig.StatType.Adder ? currentValue + kvp.Value.Value : currentValue * kvp.Value.Value);
            }
        }
    }

    private void Run_onRunStartGlobal(Run run)
    {
        RecalculateStatsAPI.GetStatCoefficients -= statHookEventHandler; // Guard against duplicate hook
        if (run.selectedDifficulty == DifficultyIndex)
        {
            DifficultyConfig.Reload();
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
