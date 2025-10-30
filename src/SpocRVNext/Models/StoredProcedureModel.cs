using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using SpocR.SpocRVNext.Data.Models;

namespace SpocR.SpocRVNext.Models;

public class StoredProcedureModel : IEquatable<StoredProcedureModel>
{
    private readonly StoredProcedure _item;

    public StoredProcedureModel()
    {
        _item = new StoredProcedure();
    }

    public StoredProcedureModel(StoredProcedure item)
    {
        _item = item;
    }

    public string Name
    {
        get => _item.Name;
        set => _item.Name = value;
    }

    public DateTime Modified
    {
        get => _item.Modified;
        set => _item.Modified = value;
    }

    public long? ModifiedTicks { get; set; }

    [JsonIgnore]
    public string SchemaName
    {
        get => _item.SchemaName;
        set => _item.SchemaName = value;
    }

    private IReadOnlyList<StoredProcedureInput>? _input;
    public IReadOnlyList<StoredProcedureInput>? Input
    {
        get => _input != null && _input.Count > 0 ? _input : null;
        set => _input = value;
    }

    private StoredProcedureContentModel _content;

    [JsonIgnore]
    public StoredProcedureContentModel Content
    {
        get => _content;
        set => _content = value;
    }

    public IReadOnlyList<StoredProcedureContentModel.ResultSet> ResultSets
        => Content?.ResultSets != null && Content.ResultSets.Any() ? Content.ResultSets : null;

    public bool Equals(StoredProcedureModel other)
    {
        return SchemaName == other?.SchemaName && Name == other?.Name;
    }

    public override bool Equals(object obj)
    {
        return Equals(obj as StoredProcedureModel);
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
