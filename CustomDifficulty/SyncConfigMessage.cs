using ProtoBuf;
using R2API.Networking.Interfaces;
using UnityEngine.Networking;
using System.IO;

public class SyncConfigMessage : INetMessage
{
    private DifficultyConfig.Stats tempStats = new();

    public SyncConfigMessage() { }

    // Host only: packages the data to send over the network
    public void Serialize(NetworkWriter writer)
    {
        using (MemoryStream stream = new MemoryStream())
        {
            Serializer.Serialize(stream, DifficultyConfig.ActiveStats);
            byte[] bytes = stream.ToArray();

            // Write the array length first, then the actual payload bytes
            writer.WriteBytesAndSize(bytes, bytes.Length);

            Log.Info($"Sent {bytes.Length} bytes.");
        }
    }

    // Unpacks incoming data into isolated temporary buffers
    public void Deserialize(NetworkReader reader)
    {
        byte[] bytes = reader.ReadBytesAndSize();
        if (bytes is null || bytes.Length == 0) return;

        using (MemoryStream stream = new MemoryStream(bytes))
        {
            // Rehydrate data into a temporary object
            tempStats = Serializer.Deserialize<DifficultyConfig.Stats>(stream);
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
