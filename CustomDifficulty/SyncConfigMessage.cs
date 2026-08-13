using System.Collections.Generic;
using R2API.Networking.Interfaces;
using UnityEngine.Networking;

public class SyncConfigMessage : INetMessage
{
    private float scalingFactor;
    private Dictionary<string, float> tempPlayerStats = new Dictionary<string, float>();
    private Dictionary<string, float> tempEnemyStats = new Dictionary<string, float>();

    // Empty constructor required for R2API
    public SyncConfigMessage() { }

    // Host only: packages the data to send over the network
    public void Serialize(NetworkWriter writer)
    {
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

    // Unpacks incoming data into isolated temporary buffers
    public void Deserialize(NetworkReader reader)
    {
        scalingFactor = reader.ReadSingle();

        uint pCount = reader.ReadPackedUInt32();
        for (int i = 0; i < pCount; i++)
        {
            tempPlayerStats[reader.ReadString()] = reader.ReadSingle();
        }

        uint eCount = reader.ReadPackedUInt32();
        for (int i = 0; i < eCount; i++)
        {
            tempEnemyStats[reader.ReadString()] = reader.ReadSingle();
        }
    }

    // Safely applies the data after deserialization completes
    public void OnReceived()
    {
        // Prevent the host from accepting or applying client-sent sync configurations
        if (NetworkServer.active) return;

        // Atomically swap the dictionaries and update the difficulty definition
        DifficultyConfig.ApplySyncedSettings(scalingFactor, tempPlayerStats, tempEnemyStats);

        Log.Info("Successfully synced custom difficulty stats from Host.");
    }
}
