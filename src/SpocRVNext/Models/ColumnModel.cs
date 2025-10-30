using SpocR.SpocRVNext.Data.Models;

namespace SpocR.SpocRVNext.Models;

public class ColumnModel
{
    private readonly Column _item;

    public ColumnModel()
    {
        _item = new Column();
    }

    public ColumnModel(Column item)
    {
        _item = item;
    }

    public string Name
    {
        get => _item.Name;
        set => _item.Name = value;
    }

    public bool? IsNullable
    {
        get => _item.IsNullable ? (bool?)true : null;
        set => _item.IsNullable = value == true;
    }

    public string SqlTypeName
    {
        get => _item.SqlTypeName;
        set => _item.SqlTypeName = value;
    }

    public int? MaxLength
    {
        get => _item.MaxLength > 0 ? (int?)_item.MaxLength : null;
        set => _item.MaxLength = value > 0 ? value.Value : 0;
    }
}
