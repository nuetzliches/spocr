using System.Text.Json.Serialization;
using SpocR.DataContext.Models;

namespace SpocR.Models;

public class StoredProcedureInputModel : ColumnModel
{
    private readonly StoredProcedureInput _item;

    public StoredProcedureInputModel() // required for json serialization
    {
        _item = new StoredProcedureInput();
    }

    public StoredProcedureInputModel(StoredProcedureInput item)
        : base(item)
    {
        _item = item;
    }

    public bool? IsTableType
    {
        get => _item.IsTableType ? (bool?)true : null;
        set => _item.IsTableType = value == true ? true : false;
    }

    public string TableTypeName
    {
        get => _item.IsTableType ? _item.UserTypeName : null;
        set => _item.UserTypeName = _item.IsTableType ? value : _item.UserTypeName;
    }

    public string TableTypeSchemaName
    {
        get => _item.IsTableType ? _item.UserTypeSchemaName : null;
        set => _item.UserTypeSchemaName = _item.IsTableType ? value : _item.UserTypeSchemaName;
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsOutput
    {
        get => _item.IsOutput;
        set => _item.IsOutput = value;
    }
}
