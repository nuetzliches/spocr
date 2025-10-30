using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SpocR.SpocRVNext.Services;
using SpocR.SpocRVNext.Extensions;
using SpocR.SpocRVNext.Models;
using SpocR.SpocRVNext.Utils;
using SpocRVNext.Configuration;

namespace SpocR.SpocRVNext.Infrastructure;

public interface IFileManager<TConfig> where TConfig : class, IVersioned
{
    TConfig Config { get; }
    bool TryOpen(string path, out TConfig? config);
}

public class FileManager<TConfig>(
    SpocrService spocr,
    string fileName,
    TConfig? defaultConfig = default
) : IFileManager<TConfig> where TConfig : class, IVersioned
{
    public TConfig? DefaultConfig
    {
        get => defaultConfig;
        set => defaultConfig = value;
    }

    private TConfig? _config;
    private TConfig? _overwritenWithConfig;
    public TConfig Config
    {
        get
        {
            if (_config == null || _overwritenWithConfig != OverwriteWithConfig)
            {
                var config = ReadAsync().GetAwaiter().GetResult();

                if (DefaultConfig == null)
                {
                    _config = config;
                }
                else
                {
                    var source = config ?? DefaultConfig;
                    _config = DefaultConfig.OverwriteWith(source);
                }

                if (OverwriteWithConfig != null)
                {
                    _config = _config == null
                        ? OverwriteWithConfig
                        : _config.OverwriteWith(OverwriteWithConfig);
                }
                _overwritenWithConfig = OverwriteWithConfig;
            }
            return _config ?? throw new InvalidOperationException($"Unable to load configuration '{fileName}'. Provide a default config or ensure the file exists.");
        }
        set => _config = value;
    }

    private TConfig? _overwriteWithConfig;
    public TConfig? OverwriteWithConfig
    {
        get => _overwriteWithConfig;
        set => _overwriteWithConfig = value;
    }

    private JsonSerializerOptions? _deserializerOptions;
    private JsonSerializerOptions DeserializerOptions
    {
        get => _deserializerOptions ??= new JsonSerializerOptions
        {
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            }
        };
    }

    private JsonSerializerOptions? _serializerOptions;
    private JsonSerializerOptions SerializerOptions
    {
        get => _serializerOptions ??= new JsonSerializerOptions()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
            Converters = {
                new JsonStringEnumConverter()
            }
        };
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

    public async Task<TConfig?> ReadAsync()
    {
        if (!Exists())
        {
            return DefaultConfig;
        }
        var path = DirectoryUtils.GetWorkingDirectory(fileName);
        var content = await File.ReadAllTextAsync(path);

        var config = JsonSerializer.Deserialize<TConfig>(content, DeserializerOptions);

        return config ?? DefaultConfig;
    }

    public async Task SaveAsync(TConfig config)
    {
        config.Version = spocr.Version;

        try
        {
            if (config is SpocR.SpocRVNext.Models.ConfigurationModel cfg)
            {
                var project = cfg.Project;
                var role = project?.Role;
                if (role != null
                    && role.Kind == RoleKindEnum.Default
                    && string.IsNullOrWhiteSpace(role.LibNamespace)
                    && project != null)
                {
                    project.Role = null!; // drop default role entirely so it is not written back to the config
                }
            }
        }
        catch (Exception)
        {
        }

        var json = JsonSerializer.Serialize(config, SerializerOptions);
        var path = DirectoryUtils.GetWorkingDirectory(fileName);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        await File.WriteAllTextAsync(path, json);
    }

    public async Task<bool> ExistsAsync()
    {
        var path = DirectoryUtils.GetWorkingDirectory(fileName);
        return await Task.FromResult(File.Exists(path));
    }

    public async Task RemoveAsync(bool dryRun = false)
    {
        if (await ExistsAsync())
        {
            if (!dryRun)
                File.Delete(fileName);
        }
    }

    public async Task ReloadAsync()
    {
        _config = null;
        _overwritenWithConfig = null;
        await Task.CompletedTask;
    }

    public bool TryOpen(string path, out TConfig? config)
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

    public void Reload()
    {
        _config = null;
        _overwritenWithConfig = null;
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
