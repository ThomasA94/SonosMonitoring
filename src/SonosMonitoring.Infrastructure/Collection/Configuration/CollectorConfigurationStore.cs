using System.Text.Json;

namespace SonosMonitoring.Infrastructure.Collection.Configuration;

/// <summary>Atomically persists one installation's technical collector settings outside deployment files.</summary>
public sealed class CollectorConfigurationStore
{
    private readonly string _path;

    /// <summary>Captures the deployment-owned storage path.</summary>
    /// <param name="path">Writable durable file, never supplied by an HTTP request.</param>
    public CollectorConfigurationStore(string path) => this._path = Path.GetFullPath(path);

    /// <summary>Loads the last saved document or uses deployment defaults on first start.</summary>
    /// <param name="defaults">Initial options, including the host-only test permission.</param>
    public CollectorConfigurationDocument Load(CollectorOptions defaults)
    {
        CollectorConfigurationDocument document;
        try
        {
            using var stream = File.OpenRead(this._path);
            document = JsonSerializer.Deserialize<CollectorConfigurationDocument>(stream)
                ?? throw new InvalidDataException("Collector settings document is empty.");
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            // Unreadable existing settings must fail startup, never silently restore deployment defaults.
            document = new CollectorConfigurationDocument(Guid.Empty, defaults);
        }
        // A saved file cannot grant the testing-only network exception.
        document.Options.AllowLoopback = defaults.AllowLoopback;
        CollectorConfigurationValidator.Validate(document.Options);
        return document;
    }

    /// <summary>Flushes a new document before replacing the old file on the same filesystem.</summary>
    /// <param name="document">Validated revision to commit.</param>
    public void Save(CollectorConfigurationDocument document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(this._path)!);
        string temporary = this._path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, document);
                stream.Flush(true);
            }
            File.Move(temporary, this._path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
