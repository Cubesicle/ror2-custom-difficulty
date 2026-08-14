using System.Collections.Generic;
using R2API.Networking.Interfaces;
using RoR2;
using UnityEngine.Networking;

public class SyncConfigMessage : INetMessage
{
    private readonly DifficultyConfig.Stats tempStats = new();

    public SyncConfigMessage() { }

    // Host only: packages the data to send over the network
    public void Serialize(NetworkWriter writer)
    {
        writer.Write(DifficultyConfig.ActiveStats.ScalingFactor);

        // Serialize Player Stats (Only Non-Zero Values)
        // We use ushort (a 16-bit integer, max 65,535) because it only takes 2 bytes of payload data.
        // A single string like "baseAttackSpeedAdd" takes ~36 bytes to send over the network.
        // By sending the list index instead of the string, we save lots of space.
        List<(ushort index, float value)> playerStatsToSend = [];
        for (ushort i = 0; i < DifficultyConfig.StatNames.Count; i++)
        {
            string key = DifficultyConfig.StatNames[i];

            // We only care about stats that have been modified. 
            // If the stat is exactly 0f, adding 0f in RecalculateStatsAPI does nothing. 
            // Skipping it entirely means we don't send useless data over the network, 
            // which prevents hitting Unity's ~1,100 byte message limit.
            if (DifficultyConfig.ActiveStats.Player.TryGetValue(key, out DifficultyConfig.Stat val) && !val.IsDefault())
            {
                playerStatsToSend.Add((i, val.Value));
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
        List<(ushort index, float value)> enemyStatsToSend = [];
        for (ushort i = 0; i < DifficultyConfig.StatNames.Count; i++)
        {
            string key = DifficultyConfig.StatNames[i];
            if (DifficultyConfig.ActiveStats.Enemy.TryGetValue(key, out DifficultyConfig.Stat val) && !val.IsDefault())
            {
                enemyStatsToSend.Add((i, val.Value));
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
                string statName = DifficultyConfig.StatNames[index];
                DifficultyConfig.Stat newStat = new(DifficultyConfig.Stat.TypeFromString(statName), val);
                tempStats.Player[DifficultyConfig.StatNames[index]] = newStat;
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
                string statName = DifficultyConfig.StatNames[index];
                DifficultyConfig.Stat newStat = new(DifficultyConfig.Stat.TypeFromString(statName), val);
                tempStats.Enemy[DifficultyConfig.StatNames[index]] = newStat;
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

        // Force all active bodies to update immediately with the new stats
        foreach (var body in CharacterBody.readOnlyInstancesList)
        {
            if (body) body.MarkAllStatsDirty();
        }

        Log.Info("Successfully synced custom difficulty stats from Host.");
    }
}
