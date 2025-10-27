using System.Collections.Generic;

namespace SpocR.SpocRVNext.SnapshotBuilder.Writers;

internal sealed class IndexDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string Fingerprint { get; set; } = string.Empty;
    public IndexParser Parser { get; set; } = new();
    public IndexStats Stats { get; set; } = new();
    public List<IndexProcedureEntry> Procedures { get; set; } = new();
    public List<IndexTableTypeEntry> TableTypes { get; set; } = new();
    public List<IndexUserDefinedTypeEntry> UserDefinedTypes { get; set; } = new();
    public int FunctionsVersion { get; set; }
    public List<IndexFunctionEntry> Functions { get; set; } = new();
}

internal sealed class IndexParser
{
    public string ToolVersion { get; set; } = string.Empty;
    public int ResultSetParserVersion { get; set; }
}

internal sealed class IndexStats
{
    public int ProcedureTotal { get; set; }
    public int ProcedureSkipped { get; set; }
    public int ProcedureLoaded { get; set; }
    public int UdttTotal { get; set; }
    public int TableTotal { get; set; }
    public int ViewTotal { get; set; }
    public int UserDefinedTypeTotal { get; set; }
    public int FunctionTotal { get; set; }
}

internal sealed class IndexProcedureEntry
{
    public string Schema { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
}

internal sealed class IndexTableTypeEntry
{
    public string Schema { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
}

internal sealed class IndexUserDefinedTypeEntry
{
    public string Schema { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
}

internal sealed class IndexFunctionEntry
{
    public string Schema { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
}
