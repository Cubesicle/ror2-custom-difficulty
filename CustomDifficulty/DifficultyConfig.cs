using BepInEx.Configuration;
using R2API;
using R2API.Networking.Interfaces;
using System.Collections.Generic;
using System.Reflection;
using System.Linq.Expressions;

public static class DifficultyConfig
{
    public static ConfigFile configFile { private get; set; }

    // Saved settings on the player's local machine
    public static ConfigEntry<float> ScalingFactorConfig;
    public static Dictionary<FieldInfo, ConfigEntry<float>> PlayerConfigs = new Dictionary<FieldInfo, ConfigEntry<float>>();
    public static Dictionary<FieldInfo, ConfigEntry<float>> EnemyConfigs = new Dictionary<FieldInfo, ConfigEntry<float>>();

    // Active settings used purely for the current multiplayer run
    public static float ActiveScalingFactor;
    public static Dictionary<string, float> ActivePlayerStats = new Dictionary<string, float>();
    public static Dictionary<string, float> ActiveEnemyStats = new Dictionary<string, float>();

    // Delegates and accessors
    public delegate float GetStatDelegate(RecalculateStatsAPI.StatHookEventArgs args);
    public delegate void SetStatDelegate(RecalculateStatsAPI.StatHookEventArgs args, float value);

    public class StatAccessor
    {
        public GetStatDelegate Get;
        public SetStatDelegate Set;
    }

    // Stores compiled delegates, mapped by field name
    public static Dictionary<string, StatAccessor> Accessors = new Dictionary<string, StatAccessor>();

    public static void Bind()
    {
        ScalingFactorConfig = configFile.Bind(
            "Run Stats",
            "Scaling Factor",
            3.0f,
            "Base difficulty time-scaling factor. (Drizzle=1.0, Rainstorm=2.0, Monsoon=3.0)"
        );

        FieldInfo[] fields = typeof(RecalculateStatsAPI.StatHookEventArgs).GetFields(BindingFlags.Public | BindingFlags.Instance);

        foreach (FieldInfo field in fields)
        {
            if (field.FieldType == typeof(float))
            {
                // Compile expression trees
                // Define the parameters
                var argsParam = Expression.Parameter(typeof(RecalculateStatsAPI.StatHookEventArgs), "args");
                var valueParam = Expression.Parameter(typeof(float), "value");

                // Access the specific field on the args instance
                var fieldAccess = Expression.Field(argsParam, field);

                // Compile the Getter: (args) => args.FieldName
                var getter = Expression.Lambda<GetStatDelegate>(fieldAccess, argsParam).Compile();

                // Compile the Setter: (args, value) => args.FieldName = value
                var setter = Expression.Lambda<SetStatDelegate>(Expression.Assign(fieldAccess, valueParam), argsParam, valueParam).Compile();

                // Store them in our dictionary for instant lookup later
                Accessors[field.Name] = new StatAccessor { Get = getter, Set = setter };


                // Bind configurations
                string description = $"Amount ADDED to {field.Name}. (Note: All fields are strictly additive. For 'mult' fields, 1.0 adds +100%.)";
                var playerConfig = configFile.Bind("Player Stats", field.Name, 0f, description);
                PlayerConfigs.Add(field, playerConfig);

                var enemyConfig = configFile.Bind("Enemy Stats", field.Name, 0f, description);
                EnemyConfigs.Add(field, enemyConfig);
            }
        }
    }

    public static void Save() => configFile.Save();
    public static void Reload() => configFile.Reload();

    public static void SyncHostConfigToClients()
    {
        ActiveScalingFactor = ScalingFactorConfig.Value;
        CustomDifficulty.DifficultyDef.scalingValue = ActiveScalingFactor;

        ActivePlayerStats.Clear();
        ActiveEnemyStats.Clear();
        foreach (var kvp in PlayerConfigs) ActivePlayerStats[kvp.Key.Name] = kvp.Value.Value;
        foreach (var kvp in EnemyConfigs) ActiveEnemyStats[kvp.Key.Name] = kvp.Value.Value;

        new SyncConfigMessage().Send(R2API.Networking.NetworkDestination.Clients);
        Log.Info("Host updated custom difficulty stats and synced to clients.");
    }
}
