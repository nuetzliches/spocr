using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpocR.Extensions;
using SpocR.Interfaces;
using SpocR.Services;
using SpocR.Utils;

namespace SpocR.Managers
{
    public class FileManager<TConfig> where TConfig : class, IVersioned
    {
        private readonly SpocrService _spocr;
        private readonly string _fileName;
        private TConfig _defaultConfig;
        public TConfig DefaultConfig
        {
            get => _defaultConfig;
            set => _defaultConfig = value;
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

        public FileManager(SpocrService spocr, string fileName, TConfig defaultConfig = default)
        {
            _spocr = spocr;
            _fileName = fileName;
            _defaultConfig = defaultConfig;
        }

        public VersionCheckResult CheckVersion()
        {
            return new VersionCheckResult(_spocr.Version, Config.Version);
        }

        public bool Exists()
        {
            var fileName = DirectoryUtils.GetWorkingDirectory(_fileName);
            return File.Exists(fileName);
        }

        public TConfig Read()
        {
            if (!Exists())
            {
                return null;
            }
            var fileName = DirectoryUtils.GetWorkingDirectory(_fileName);
            var content = File.ReadAllText(fileName);

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
            config.Version = _spocr.Version;

            var json = JsonSerializer.Serialize(config, jsonSettings);
            var fileName = DirectoryUtils.GetWorkingDirectory(_fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(fileName));
            File.WriteAllText(fileName, json);
        }

        public void Remove(bool dryRun = false)
        {
            if (Exists())
            {
                if (!dryRun)
                    File.Delete(_fileName);
            }
        }
    }

    public class VersionCheckResult
    {

        public readonly Version SpocRVersion;
        public readonly Version ConfigVersion;
        public bool DoesMatch => SpocRVersion == ConfigVersion;

        public VersionCheckResult(Version spocrVersion, Version configVersion)
        {
            SpocRVersion = spocrVersion;
            ConfigVersion = configVersion;
        }
    }
}