using R2API.Networking.Interfaces;
using UnityEngine.Networking;

public class SyncConfigMessage : INetMessage
{
    private DifficultyConfig.Stats tempStats = new();

    // Empty constructor required for R2API
    public SyncConfigMessage() { }

    // Host only: packages the data to send over the network
    public void Serialize(NetworkWriter writer)
    {
        writer.Write(DifficultyConfig.ActiveStats.ScalingFactor);
        writer.WritePackedUInt32((uint)DifficultyConfig.ActiveStats.Player.Count);
        foreach (var kvp in DifficultyConfig.ActiveStats.Player)
        {
            writer.Write(kvp.Key);
            writer.Write(kvp.Value);
        }
        writer.WritePackedUInt32((uint)DifficultyConfig.ActiveStats.Enemy.Count);
        foreach (var kvp in DifficultyConfig.ActiveStats.Enemy)
        {
            writer.Write(kvp.Key);
            writer.Write(kvp.Value);
        }
    }

    // Unpacks incoming data into isolated temporary buffers
    public void Deserialize(NetworkReader reader)
    {
        tempStats.ScalingFactor = reader.ReadSingle();

        uint pCount = reader.ReadPackedUInt32();
        for (int i = 0; i < pCount; i++)
        {
            tempStats.Player[reader.ReadString()] = reader.ReadSingle();
        }

        uint eCount = reader.ReadPackedUInt32();
        for (int i = 0; i < eCount; i++)
        {
            tempStats.Enemy[reader.ReadString()] = reader.ReadSingle();
        }
    }

    // Safely applies the data after deserialization completes
    public void OnReceived()
    {
        // Prevent the host from accepting or applying client-sent sync configurations
        if (NetworkServer.active) return;

        // Atomically swap the dictionaries and update the difficulty definition
        DifficultyConfig.ApplySyncedSettings(tempStats);

        Log.Info("Successfully synced custom difficulty stats from Host.");
    }
}
