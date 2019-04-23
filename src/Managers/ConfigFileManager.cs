using System.IO;
using Newtonsoft.Json;
using SpocR.Models;
using SpocR.Serialization;
using SpocR.Utils;

namespace SpocR.Managers
{
    public class ConfigFileManager
    {
        private ConfigurationModel _config;
        public ConfigurationModel Config
        {
            get => _config ?? (_config = Read());
            set => _config = value;
        }

        public bool Exists()
        {
            var fileName = DirectoryUtils.GetWorkingDirectory(Configuration.ConfigurationFile);
            return File.Exists(fileName);
        }

        public ConfigurationModel Read()
        {
            if (!Exists())
            {
                return null;
            }
            var fileName = DirectoryUtils.GetWorkingDirectory(Configuration.ConfigurationFile);
            var content = File.ReadAllText(fileName);
            return JsonConvert.DeserializeObject<ConfigurationModel>(content);
        }

        public void Save(ConfigurationModel config)
        {
            var jsonSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                ContractResolver = new SerializeContractResolver()
            };
            var json = JsonConvert.SerializeObject(new ConfigurationJsonModel(config), Formatting.Indented, jsonSettings);
            var fileName = DirectoryUtils.GetWorkingDirectory(Configuration.ConfigurationFile);
            Directory.CreateDirectory(Path.GetDirectoryName(fileName));
            File.WriteAllText(fileName, json);
        }

        public void Remove(bool dryRun)
        {
            if (Exists())
            {
                if (!dryRun)
                    File.Delete(Configuration.ConfigurationFile);
            }
        }
    }
}