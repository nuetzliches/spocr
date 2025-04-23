using System.Collections.Generic;
using System.Linq;
using SpocR.Models;
using static SpocR.Contracts.Definition;

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
            return storedProcedure.Output?.Any() ?? false;
        }

        internal static bool IsScalarResult(this StoredProcedure storedProcedure)
        {
            // TODO: i.Output?.Count() -> Implement a Property "IsScalar" and "IsJson"
            return storedProcedure.ReadWriteKind == ReadWriteKindEnum.Read && storedProcedure.Output?.Count() == 1;
        }

        internal static string GetOutputTypeName(this StoredProcedure storedProcedure)
        {
            return storedProcedure.IsDefaultOutput() ? "Output" : $"{storedProcedure.Name}Output";
        }

    }
}
