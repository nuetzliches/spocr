using System.IO;
using Newtonsoft.Json;
using SpocR.Models;
using SpocR.Serialization;

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
            return File.Exists(Configuration.ConfigurationFile);
        }

        public ConfigurationModel Read()
        {
            if (!Exists())
            {
                return null;
            }
            var content = File.ReadAllText(Configuration.ConfigurationFile);
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
            File.WriteAllText(Configuration.ConfigurationFile, json);
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