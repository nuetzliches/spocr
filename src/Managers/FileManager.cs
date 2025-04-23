using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpocR.Extensions;
using SpocR.Interfaces;
using SpocR.Services;
using SpocR.Utils;

namespace SpocR.Managers;

public class FileManager<TConfig>(
    SpocrService spocr,
    string fileName,
    TConfig defaultConfig = default
) where TConfig : class, IVersioned
{
    public TConfig DefaultConfig
    {
        get => defaultConfig;
        set => defaultConfig = value;
    }

    private TConfig _config;
    private TConfig _overwritenWithConfig;
    public TConfig Config
    {
        get
        {
            if (_config == null || _overwritenWithConfig != OverwriteWithConfig)
            {
                _config = DefaultConfig == null
                    ? Read()
                    : DefaultConfig.OverwriteWith<TConfig>(Read());

                if (OverwriteWithConfig != null)
                {
                    _config = _config.OverwriteWith<TConfig>(OverwriteWithConfig);
                }
                _overwritenWithConfig = OverwriteWithConfig;
            }
            return _config;
        }
        set => _config = value;
    }

    private TConfig _overwriteWithConfig;
    public TConfig OverwriteWithConfig
    {
        get => _overwriteWithConfig;
        set => _overwriteWithConfig = value;
    }

    public VersionCheckResult CheckVersion()
    {
        return new VersionCheckResult(spocr.Version, Config.Version);
    }

    public bool Exists()
    {
        var path = DirectoryUtils.GetWorkingDirectory(fileName);
        return File.Exists(path);
    }

    public TConfig Read()
    {
        if (!Exists())
        {
            return null;
        }
        var path = DirectoryUtils.GetWorkingDirectory(fileName);
        var content = File.ReadAllText(path);

        var options = new JsonSerializerOptions
        {
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            }
        };

        var config = JsonSerializer.Deserialize<TConfig>(content, options);

        return config;
    }

    public void Save(TConfig config)
    {
        var jsonSettings = new JsonSerializerOptions()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
            Converters = {
                new JsonStringEnumConverter()
            }
        };

        // Overwrite with current SpocR-Version
        config.Version = spocr.Version;

        var json = JsonSerializer.Serialize(config, jsonSettings);
        var path = DirectoryUtils.GetWorkingDirectory(fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, json);
    }

    public void Remove(bool dryRun = false)
    {
        if (Exists())
        {
            if (!dryRun)
                File.Delete(fileName);
        }
    }

    /// <summary>
    /// Versucht, die Konfiguration aus einem bestimmten Pfad zu laden.
    /// </summary>
    /// <param name="path">Der Verzeichnispfad, in dem die Konfigurationsdatei gesucht werden soll.</param>
    /// <param name="config">Die geladene Konfiguration, falls erfolgreich.</param>
    /// <returns>True, wenn die Konfiguration erfolgreich geladen wurde, andernfalls False.</returns>
    public bool TryOpen(string path, out TConfig config)
    {
        config = null;
        try
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            var originalWorkingDirectory = DirectoryUtils.GetApplicationRoot();
            DirectoryUtils.SetBasePath(path);

            if (!Exists())
            {
                // Pfad zurücksetzen
                DirectoryUtils.SetBasePath(originalWorkingDirectory);
                return false;
            }

            config = Config;
            return config != null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

public class VersionCheckResult(
    Version spocrVersion,
    Version configVersion
)
{
    public readonly Version SpocRVersion = spocrVersion;
    public readonly Version ConfigVersion = configVersion;
    public bool DoesMatch => SpocRVersion == ConfigVersion;
}
