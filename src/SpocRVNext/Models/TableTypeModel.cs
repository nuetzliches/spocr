using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using SpocR.SpocRVNext.Data.Models;

namespace SpocR.SpocRVNext.Models;

public class TableTypeModel : IEquatable<TableTypeModel>
{
    private readonly TableType _item;

    public TableTypeModel()
    {
        _item = new TableType();
    }

    public TableTypeModel(TableType item, List<Column> columns)
    {
        _item = item;
        Columns = columns.Select(c => new ColumnModel(c)).ToList();
    }

    public string Name
    {
        get => _item.Name;
        set => _item.Name = value;
    }

    [JsonIgnore]
    public string SchemaName
    {
        get => _item.SchemaName;
        set => _item.SchemaName = value;
    }

    public List<ColumnModel> _columns;
    public List<ColumnModel> Columns
    {
        get => _columns;
        set => _columns = value;
    }

    public bool Equals(TableTypeModel other)
    {
        return SchemaName == other?.SchemaName && Name == other?.Name;
    }

    public override bool Equals(object obj)
    {
        return Equals(obj as TableTypeModel);
    }

    public override int GetHashCode()
    {
        throw new NotImplementedException();
    }

    public override string ToString()
    {
        return "[SchemaName].[Name]";
    }
}
