using System.Collections.Generic;
using System.Linq;
using SpocR.DataContext.Models;

namespace SpocR.Models;

public class SchemaModel
{
    private readonly Schema _item;

    public SchemaModel() // required for json serialization
    {
        _item = new Schema();
    }

    public SchemaModel(Schema item)
    {
        _item = item;
    }

    public string Name
    {
        get => _item.Name;
        set => _item.Name = value;
    }

    public SchemaStatusEnum Status { get; set; } = SchemaStatusEnum.Build;

    private IEnumerable<StoredProcedureModel> _storedProcedures;
    public IEnumerable<StoredProcedureModel> StoredProcedures
    {
        get => _storedProcedures?.Any() ?? false ? _storedProcedures : null;
        set => _storedProcedures = value;
    }

    private IEnumerable<TableTypeModel> _tableTypes;
    public IEnumerable<TableTypeModel> TableTypes
    {
        get => _tableTypes?.Any() ?? false ? _tableTypes : null;
        set => _tableTypes = value;
    }
}

public enum SchemaStatusEnum
{
    Undefined,
    Pull,
    Build,
    Ignore
}
