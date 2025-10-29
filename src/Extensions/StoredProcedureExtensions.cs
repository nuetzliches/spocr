using System.Collections.Generic;
using System.Linq;
using SpocR.Models;
using static SpocR.SpocRVNext.Core.Definition;

namespace SpocR.Extensions
{
    internal static class StoredProcedureExtensions
    {
        internal static bool HasOutputs(this StoredProcedure storedProcedure)
        {
            return storedProcedure.Input?.Any(i => i.IsOutput) ?? false;
        }

        internal static bool HasInputs(this StoredProcedure storedProcedure)
        {
            return storedProcedure.Input?.Any() ?? false;
        }

        internal static bool IsDefaultOutput(this StoredProcedure storedProcedure)
        {
            var resultIdOutput = "@ResultId";
            var defaultOutputs = new[] { resultIdOutput, "@RecordId", "@RowVersion" };

            return (storedProcedure.GetOutputs()?.Any(o => o.Name.Equals(resultIdOutput)) ?? false) // "@ResultId" is required
                && (storedProcedure.GetOutputs()?.All(o => defaultOutputs.Contains(o.Name)) ?? false);
        }

        internal static IEnumerable<StoredProcedureInputModel> GetOutputs(this StoredProcedure storedProcedure)
        {
            return storedProcedure.Input?.Where(i => i.IsOutput);
        }

        internal static bool HasResult(this StoredProcedure storedProcedure)
        {
            var first = storedProcedure.ResultSets?.FirstOrDefault();
            if (first == null) return false;
            if (first.ReturnsJson) return true; // JSON considered a result even without inferred columns
            return first.Columns?.Any() ?? false;
        }

        internal static bool IsScalarResult(this StoredProcedure storedProcedure)
        {
            var first = storedProcedure.ResultSets?.FirstOrDefault();
            if (first == null || first.ReturnsJson) return false;
            var columnCount = first.Columns?.Count ?? 0;
            return columnCount == 1;
        }

        internal static string GetOutputTypeName(this StoredProcedure storedProcedure)
        {
            // Kept for compatibility; naming unchanged
            return storedProcedure.IsDefaultOutput() ? "Output" : $"{storedProcedure.Name}Output";
        }

    }
}
