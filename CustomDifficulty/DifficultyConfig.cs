using BepInEx.Configuration;
using R2API;
using R2API.Networking.Interfaces;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Linq.Expressions;

public static class DifficultyConfig
{
    public class Stats
    {
        public float ScalingFactor;
        public Dictionary<string, float> Player = new Dictionary<string, float>();
        public Dictionary<string, float> Enemy = new Dictionary<string, float>();
    }

    public static ConfigFile? configFile { private get; set; }

    // We use a List to maintain a strict, ordered record of stat field names.
    // Because C# Reflection (GetFields) does NOT guarantee a deterministic order 
    // across different machines or OS environments, we explicitly sort the fields 
    // alphabetically. This ensures both the Host and the Client generate this exact 
    // same list in the exact same order to prevent network desyncs.
    public static List<string> StatNames = new List<string>();

    // Saved settings on the player's local machine
    public static ConfigEntry<float>? ScalingFactorConfig;
    public static Dictionary<string, ConfigEntry<float>> PlayerConfigs = new Dictionary<string, ConfigEntry<float>>();
    public static Dictionary<string, ConfigEntry<float>> EnemyConfigs = new Dictionary<string, ConfigEntry<float>>();

    // Active settings used purely for the current multiplayer run
    public static Stats ActiveStats = new();

    // Delegates and accessors
    public delegate float GetStatDelegate(RecalculateStatsAPI.StatHookEventArgs args);
    public delegate void SetStatDelegate(RecalculateStatsAPI.StatHookEventArgs args, float value);

    public class StatAccessor
    {
        public required GetStatDelegate Get;
        public required SetStatDelegate Set;
    }

    // Stores compiled delegates, mapped by field name
    public static Dictionary<string, StatAccessor> Accessors = new Dictionary<string, StatAccessor>();

    public static void Bind()
    {
        if (configFile is null) return;

        ScalingFactorConfig = configFile.Bind(
            "Run Stats",
            "Scaling Factor",
            1f,
            "Base difficulty time-scaling factor. (Drizzle=1.0, Rainstorm=2.0, Monsoon=3.0)"
        );

        // Fetch and explicitly sort fields alphabetically to guarantee network determinism
        FieldInfo[] fields = typeof(RecalculateStatsAPI.StatHookEventArgs)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(f => f.Name)
            .ToArray();

        foreach (FieldInfo field in fields)
        {
            if (field.FieldType == typeof(float))
            {
                // Add the field name to our ordered list as we iterate.
                StatNames.Add(field.Name);

                var argsParam = Expression.Parameter(typeof(RecalculateStatsAPI.StatHookEventArgs), "args");
                var valueParam = Expression.Parameter(typeof(float), "value");
                var fieldAccess = Expression.Field(argsParam, field);

                var getter = Expression.Lambda<GetStatDelegate>(fieldAccess, argsParam).Compile();
                var setter = Expression.Lambda<SetStatDelegate>(Expression.Assign(fieldAccess, valueParam), argsParam, valueParam).Compile();
                Accessors[field.Name] = new StatAccessor { Get = getter, Set = setter };

                string description = $"Amount ADDED to {field.Name}. (Note: All fields are strictly additive. For 'mult' fields, 1.0 adds +100%.)";

                var playerConfig = configFile.Bind("Player Stats", field.Name, 0f, description);
                PlayerConfigs.Add(field.Name, playerConfig);

                var enemyConfig = configFile.Bind("Enemy Stats", field.Name, 0f, description);
                EnemyConfigs.Add(field.Name, enemyConfig);
            }
        }
    }

    public static void Save()
    {
        if (!isHost()) return;

        configFile?.Save();
        SyncHostConfigToClients();
    }

    public static void Reload()
    {
        if (!isHost()) return;

        configFile?.Reload();
        SyncHostConfigToClients();
    }

    public static void ApplySyncedSettings(Stats newStats)
    {
        ActiveStats = newStats;
        if (CustomDifficulty.DifficultyDef is { } def)
        {
            def.scalingValue = ActiveStats.ScalingFactor;
        }
    }

    private static void SyncHostConfigToClients()
    {
        if (!isHost() || ScalingFactorConfig is null) return;

        Stats newStats = new Stats { ScalingFactor = ScalingFactorConfig.Value };
        foreach (var kvp in PlayerConfigs) newStats.Player[kvp.Key] = kvp.Value.Value;
        foreach (var kvp in EnemyConfigs) newStats.Enemy[kvp.Key] = kvp.Value.Value;

        ApplySyncedSettings(newStats);

        new SyncConfigMessage().Send(R2API.Networking.NetworkDestination.Clients);

        Log.Info("Host updated custom difficulty stats and synced to clients.");
    }

    private static bool isHost()
    {
        return UnityEngine.Networking.NetworkServer.active;
    }
}
