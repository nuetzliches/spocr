using SpocR.DataContext.Models;

namespace SpocR.Models;

public class ColumnModel
{
    private readonly Column _item;

    public ColumnModel()
    {
        // required for JSON Serializer
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
        set => _item.IsNullable = value == true ? true : false;
    }

    public string SqlTypeName
    {
        get => _item.SqlTypeName;
        set => _item.SqlTypeName = value;
    }

    public int? MaxLength
    {
        get => _item.MaxLength > 0 ? (int?)_item.MaxLength : null;
        set => _item.MaxLength = (int)(value > 0 ? value : 0);
    }
}
