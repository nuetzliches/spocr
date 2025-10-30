namespace SpocR.SpocRVNext.Models;

public class OutputModel
{
    public string Namespace { get; set; } = string.Empty;
    public DataContextModel DataContext { get; set; } = new();
}
