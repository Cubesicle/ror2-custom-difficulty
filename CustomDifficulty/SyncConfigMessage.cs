using R2API.Networking.Interfaces;
using UnityEngine.Networking;

public class SyncConfigMessage : INetMessage
{
    // Empty constructor required for R2API
    public SyncConfigMessage() { }

    // Host only: packages the data to send over the internet
    public void Serialize(NetworkWriter writer)
    {
        // Read directly from the static DifficultyConfig class
        writer.Write(DifficultyConfig.ActiveScalingFactor);

        writer.WritePackedUInt32((uint)DifficultyConfig.ActivePlayerStats.Count);
        foreach (var kvp in DifficultyConfig.ActivePlayerStats)
        {
            writer.Write(kvp.Key);
            writer.Write(kvp.Value);
        }

        writer.WritePackedUInt32((uint)DifficultyConfig.ActiveEnemyStats.Count);
        foreach (var kvp in DifficultyConfig.ActiveEnemyStats)
        {
            writer.Write(kvp.Key);
            writer.Write(kvp.Value);
        }
    }

    // Client only: unpacks the data when they receive it
    public void Deserialize(NetworkReader reader)
    {
        // Clear out the client's current active stats and overwrite them directly
        DifficultyConfig.ActiveScalingFactor = reader.ReadSingle();

        DifficultyConfig.ActivePlayerStats.Clear();

        uint pCount = reader.ReadPackedUInt32();
        for (int i = 0; i < pCount; i++)
        {
            DifficultyConfig.ActivePlayerStats[reader.ReadString()] = reader.ReadSingle();
        }

        DifficultyConfig.ActiveEnemyStats.Clear();

        uint eCount = reader.ReadPackedUInt32();
        for (int i = 0; i < eCount; i++)
        {
            DifficultyConfig.ActiveEnemyStats[reader.ReadString()] = reader.ReadSingle();
        }
    }

    // Client only: what happens after deserialization is finished
    public void OnReceived()
    {
        if (NetworkServer.active) return;

        if (CustomDifficulty.DifficultyDef is { } def)
        {
            def.scalingValue = DifficultyConfig.ActiveScalingFactor;
        }

        Log.Info("Successfully synced custom difficulty stats from Host.");
    }
}
