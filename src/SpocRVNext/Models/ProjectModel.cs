using System.Collections.Generic;

namespace SpocR.SpocRVNext.Models;

public class ProjectModel
{
    public DataBaseModel DataBase { get; set; } = new();
    public OutputModel Output { get; set; } = new();
    public SchemaStatusEnum DefaultSchemaStatus { get; set; } = SchemaStatusEnum.Build;
    public List<string> IgnoredSchemas { get; set; } = new();
    public List<string> IgnoredProcedures { get; set; } = new();
    public JsonTypeLogLevel JsonTypeLogLevel { get; set; } = JsonTypeLogLevel.Detailed;
}

public enum JsonTypeLogLevel
{
    Detailed = 0,
    SummaryOnly = 1,
    Off = 2
}
