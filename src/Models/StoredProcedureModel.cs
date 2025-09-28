using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using SpocR.DataContext.Models;

namespace SpocR.Models;

public class StoredProcedureModel : IEquatable<StoredProcedureModel>
{
    private readonly StoredProcedure _item;

    public StoredProcedureModel() // required for json serialization
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

    [JsonIgnore]
    public string SchemaName
    {
        get => _item.SchemaName;
        set => _item.SchemaName = value;
    }

    private IEnumerable<StoredProcedureInputModel> _input;
    public IEnumerable<StoredProcedureInputModel> Input
    {
        get => _input?.Any() ?? false ? _input : null;
        set => _input = value;
    }

    private IEnumerable<StoredProcedureOutputModel> _output;
    public IEnumerable<StoredProcedureOutputModel> Output
    {
        get => _output?.Any() ?? false ? _output : null;
        set => _output = value;
    }

    private StoredProcedureContentModel _content;

    [JsonIgnore]
    public StoredProcedureContentModel Content
    {
        get => _content;
        set => _content = value;
    }

    [JsonIgnore]
    public bool ReturnsJson => Content?.ReturnsJson ?? false;

    [JsonIgnore]
    public bool ReturnsJsonArray => Content?.ReturnsJsonArray ?? false;

    [JsonIgnore]
    public IReadOnlyList<StoredProcedureContentModel.JsonColumn> JsonColumns => Content?.JsonColumns;

    [JsonIgnore]
    public bool ReturnsJsonWithoutArrayWrapper => Content?.ReturnsJsonWithoutArrayWrapper ?? false;

    // public IEnumerable<StoredProcedureInputModel> Input { get; set; }
    // public IEnumerable<StoredProcedureOutputModel> Output { get; set; }

    public bool Equals(StoredProcedureModel other)
    {
        return SchemaName == other.SchemaName && Name == other.Name;
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
        return $"[SchemaName].[Name]";
    }
}
