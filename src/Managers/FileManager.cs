using System;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using SpocR.Extensions;
using SpocR.Serialization;
using SpocR.Utils;

namespace SpocR.Managers
{
    public class FileManager<TConfig> where TConfig : class
    {
        private readonly string _fileName;
        private TConfig _config;
        private TConfig _overwritenWithConfig;
        public TConfig Config
        {
            get
            {
                if(_config == null || _overwritenWithConfig != OverwriteWithConfig) 
                {
                    _config = Read();
                    if(OverwriteWithConfig != null) 
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

        public FileManager(string fileName)
        {
            _fileName = fileName;
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
            return JsonConvert.DeserializeObject<TConfig>(content);
        }

        public void Save(TConfig config)
        {
            var jsonSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                ContractResolver = new SerializeContractResolver()
            };
            var json = JsonConvert.SerializeObject(config, Formatting.Indented, jsonSettings);
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
}