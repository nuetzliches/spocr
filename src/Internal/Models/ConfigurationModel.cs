using System;
using System.Collections.Generic;
using SpocR.Internal.Models;

namespace SpocR.Internal.Models
{
    public class ConfigurationModel
    {
        public Version Version { get; set; }
        public DateTime Modified { get; set; }
        public ProjectModel Project { get; set; }
        public IEnumerable<SchemaModel> Schema { get; set; }
    }

    public class ProjectModel
    {
        public string Namespace { get; set; }
        public DataBaseModel DataBase { get; set; }
        public IEnumerable<StructureModel> Structure { get; set; }
    }

    public class DataBaseModel
    {
        public string RuntimeConnectionStringIdentifier { get; set; }
        public string ConnectionString { get; set; }
    }

    public class StructureModel
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public IEnumerable<StructureModel> Children { get; set; }
    }

    public class ConfigurationJsonModel
    {
        private readonly ConfigurationModel _item;
        public ConfigurationJsonModel(ConfigurationModel item)
        {
            _item = item;
        }
        public string Version => $"{_item.Version.Major}.{_item.Version.Minor}.{_item.Version.Build}";
        public DateTime Modified => _item.Modified;
        public ProjectModel Project => _item.Project;
        public IEnumerable<SchemaModel> Schema => _item.Schema;
    }
}