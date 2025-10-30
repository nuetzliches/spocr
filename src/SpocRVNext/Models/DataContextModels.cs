namespace SpocR.SpocRVNext.Models;

public class DataContextModel : IDirectoryModel
{
    public string Path { get; set; } = string.Empty;
    public DataContextInputsModel Inputs { get; set; } = new();
    public DataContextOutputsModel Outputs { get; set; } = new();
    public DataContextModelsModel Models { get; set; } = new();
    public DataContextStoredProceduresModel StoredProcedures { get; set; } = new();
    public DataContextTableTypesModel TableTypes { get; set; } = new();
}

public class DataContextInputsModel : IDirectoryModel
{
    public string Path { get; set; } = string.Empty;
}

public class DataContextOutputsModel : IDirectoryModel
{
    public string Path { get; set; } = string.Empty;
}

public class DataContextModelsModel : IDirectoryModel
{
    public string Path { get; set; } = string.Empty;
}

public class DataContextTableTypesModel : IDirectoryModel
{
    public string Path { get; set; } = string.Empty;
}

public class DataContextStoredProceduresModel : IDirectoryModel
{
    public string Path { get; set; } = string.Empty;
}

public interface IDirectoryModel
{
    string Path { get; set; }
}
