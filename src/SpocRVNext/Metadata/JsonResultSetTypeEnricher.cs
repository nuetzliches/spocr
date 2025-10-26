using System.Collections.Generic;

namespace SpocR.SpocRVNext.Metadata;

/// <summary>
/// Legacy placeholder retained for binary compatibility. JSON column typing is now captured directly in the snapshot writer,
/// so enrichment at metadata-load time is no longer required.
/// </summary>
internal static class JsonResultSetTypeEnricher
{
    public static void Enrich(List<ResultSetDescriptor> resultSets) { /* no-op */ }
}
