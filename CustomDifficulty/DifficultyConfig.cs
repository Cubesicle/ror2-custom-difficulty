using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using BepInEx.Configuration;
using R2API;
using R2API.Networking.Interfaces;

public static class DifficultyConfig
{
    // The enum can be casted to a float to get the default value.
    public enum StatType
    {
        Adder = 0,
        Multiplier = 1
    }

    public class Stat(StatType statType, float value)
    {
        public StatType Type { get; private set; } = statType;
        public float Value = value;

        public static StatType TypeFromString(string str)
        {
            // Assume that all stat names end with either "add" or "mult" lol.
            return str.EndsWith("add", StringComparison.OrdinalIgnoreCase) ? StatType.Adder : StatType.Multiplier;
        }

        public bool IsDefault()
        {
            return Value == (float)Type;
        }
    }

    public class Stats
    {
        public float ScalingFactor;
        public Dictionary<string, Stat> Player = [];
        public Dictionary<string, Stat> Enemy = [];
    }

    public static ConfigFile? ConfigFile { private get; set; }

    // We use a List to maintain a strict, ordered record of stat field names.
    // Because C# Reflection (GetFields) does NOT guarantee a deterministic order 
    // across different machines or OS environments, we explicitly sort the fields 
    // alphabetically. This ensures both the Host and the Client generate this exact 
    // same list in the exact same order to prevent network desyncs.
    public static List<string> StatNames = [];

    // Saved settings on the player's local machine
    public static ConfigEntry<float>? ScalingFactorConfig;
    public static Dictionary<string, ConfigEntry<float>> PlayerConfigs = [];
    public static Dictionary<string, ConfigEntry<float>> EnemyConfigs = [];

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
    public static Dictionary<string, StatAccessor> Accessors = [];

    public static void Bind()
    {
        if (ConfigFile is null) return;

        ScalingFactorConfig = ConfigFile.Bind(
            "Run Stats",
            "Scaling Factor",
            1f,
            "Base difficulty time-scaling factor. (Drizzle=1.0, Rainstorm=2.0, Monsoon=3.0)"
        );

        // Fetch and explicitly sort fields alphabetically to guarantee network determinism
        FieldInfo[] fields = [.. typeof(RecalculateStatsAPI.StatHookEventArgs)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(f => f.Name)];

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

                StatType statType = Stat.TypeFromString(field.Name);
                string operation = statType == StatType.Adder ? "ADDED" : "MULTIPLIED";
                string description = $"Amount {operation} to {field.Name}.";

                var playerConfig = ConfigFile.Bind("Player Stats", field.Name, (float)statType, description);
                PlayerConfigs.Add(field.Name, playerConfig);

                var enemyConfig = ConfigFile.Bind("Enemy Stats", field.Name, (float)statType, description);
                EnemyConfigs.Add(field.Name, enemyConfig);
            }
        }
    }

    public static void Save()
    {
        if (!IsHost()) return;

        ConfigFile?.Save();
        SyncHostConfigToClients();
    }

    public static void Reload()
    {
        if (!IsHost()) return;

        ConfigFile?.Reload();
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
        if (!IsHost() || ScalingFactorConfig is null) return;

        Stats newStats = new() { ScalingFactor = ScalingFactorConfig.Value };
        foreach (var kvp in PlayerConfigs) newStats.Player[kvp.Key] = new Stat(Stat.TypeFromString(kvp.Key), kvp.Value.Value);
        foreach (var kvp in EnemyConfigs) newStats.Enemy[kvp.Key] = new Stat(Stat.TypeFromString(kvp.Key), kvp.Value.Value);

        ApplySyncedSettings(newStats);

        new SyncConfigMessage().Send(R2API.Networking.NetworkDestination.Clients);

        Log.Info("Host updated custom difficulty stats and synced to clients.");
    }

    private static bool IsHost()
    {
        return UnityEngine.Networking.NetworkServer.active;
    }
}
