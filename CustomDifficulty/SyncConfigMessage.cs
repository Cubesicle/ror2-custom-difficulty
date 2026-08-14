using R2API.Networking.Interfaces;
using UnityEngine.Networking;
using System.Collections.Generic;

public class SyncConfigMessage : INetMessage
{
    private DifficultyConfig.Stats tempStats = new();

    public SyncConfigMessage() { }

    // Host only: packages the data to send over the network
    public void Serialize(NetworkWriter writer)
    {
        writer.Write(DifficultyConfig.ActiveStats.ScalingFactor);

        // Serialize Player Stats (Only Non-Zero Values)
        // We use ushort (a 16-bit integer, max 65,535) because it only takes 2 bytes of payload data.
        // A single string like "baseAttackSpeedAdd" takes ~36 bytes to send over the network.
        // By sending the list index instead of the string, we save lots of space.
        List<(ushort index, float value)> playerStatsToSend = new();
        for (ushort i = 0; i < DifficultyConfig.StatNames.Count; i++)
        {
            string key = DifficultyConfig.StatNames[i];

            // We only care about stats that have been modified. 
            // If the stat is exactly 0f, adding 0f in RecalculateStatsAPI does nothing. 
            // Skipping it entirely means we don't send useless data over the network, 
            // which prevents hitting Unity's ~1,100 byte message limit.
            if (DifficultyConfig.ActiveStats.Player.TryGetValue(key, out float val) && val != 0f)
            {
                playerStatsToSend.Add((i, val));
            }
        }

        writer.WritePackedUInt32((uint)playerStatsToSend.Count);
        foreach (var (index, val) in playerStatsToSend)
        {
            // index = 2 bytes. val (float) = 4 bytes. 
            // Total cost per modified stat is just 6 bytes.
            writer.Write(index);
            writer.Write(val);
        }

        // Serialize Enemy Stats (Only Non-Zero Values)
        List<(ushort index, float value)> enemyStatsToSend = new();
        for (ushort i = 0; i < DifficultyConfig.StatNames.Count; i++)
        {
            string key = DifficultyConfig.StatNames[i];
            if (DifficultyConfig.ActiveStats.Enemy.TryGetValue(key, out float val) && val != 0f)
            {
                enemyStatsToSend.Add((i, val));
            }
        }

        writer.WritePackedUInt32((uint)enemyStatsToSend.Count);
        foreach (var (index, val) in enemyStatsToSend)
        {
            writer.Write(index);
            writer.Write(val);
        }
    }

    // Unpacks incoming data into isolated temporary buffers
    public void Deserialize(NetworkReader reader)
    {
        tempStats.ScalingFactor = reader.ReadSingle();

        // Read Player Stats
        uint pCount = reader.ReadPackedUInt32();
        for (int i = 0; i < pCount; i++)
        {
            // Read our 2-byte index back into memory
            ushort index = reader.ReadUInt16();
            float val = reader.ReadSingle();

            // We use the index we received to look up the actual string name locally.
            // The bounds check (index < Count) ensures a mismatched mod version doesn't crash the client.
            if (index < DifficultyConfig.StatNames.Count)
            {
                tempStats.Player[DifficultyConfig.StatNames[index]] = val;
            }
        }

        // Read Enemy Stats
        uint eCount = reader.ReadPackedUInt32();
        for (int i = 0; i < eCount; i++)
        {
            ushort index = reader.ReadUInt16();
            float val = reader.ReadSingle();

            if (index < DifficultyConfig.StatNames.Count)
            {
                tempStats.Enemy[DifficultyConfig.StatNames[index]] = val;
            }
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
