using System;
using System.Collections.Generic;
using SpocR.SpocRVNext.Metadata;
using SpocR.SpocRVNext.Utils;
using Xunit;

namespace SpocR.Tests;

public class ResultSetNamingConflictTests
{
    /// <summary>
    /// Verifies that when multiple result sets would receive the same suggested base name (e.g. same first table),
    /// the snapshot naming logic applies incremental numeric suffixes (Users, Users1, Users2 ...).
    /// This replays the logic in SchemaMetadataProvider: generic initial name via ResultSetNaming.DeriveName(),
    /// then suggestion override with duplicate avoidance appending an increasing integer starting at 1.
    /// We intentionally simulate three duplicates to ensure cascading suffixing works.
    /// </summary>
    [Fact]
    public void DuplicateSuggestedNames_AreAssignedIncrementalSuffixes()
    {
        // Arrange
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var finalNames = new List<string>();
        const string suggested = "Users"; // simulated table name suggestion from ResultSetNameResolver

        for (int i = 0; i < 3; i++)
        {
            // Phase 1: derive generic name (ResultSet{n}) – using zero fields to force generic fallback
            var fields = Array.Empty<FieldDescriptor>();
            var rsName = ResultSetNaming.DeriveName(i, fields, usedNames); // adds ResultSet{n} into usedNames

            // Phase 2: suggestion override (mirrors SchemaMetadataProvider code block)
            if (!string.IsNullOrWhiteSpace(suggested) && rsName.StartsWith("ResultSet", StringComparison.OrdinalIgnoreCase))
            {
                var baseNameUnique = NamePolicy.Sanitize(suggested);
                var final = baseNameUnique;
                if (usedNames.Contains(final))
                {
                    int suffix = 1;
                    while (usedNames.Contains(final))
                    {
                        final = baseNameUnique + suffix.ToString();
                        suffix++;
                    }
                }
                rsName = final;
            }

            // Add overridden (or original) name so subsequent iterations see it.
            usedNames.Add(rsName);
            finalNames.Add(rsName);
        }

        // Assert
        Assert.Collection(finalNames,
            n => Assert.Equal("Users", n),
            n => Assert.Equal("Users1", n),
            n => Assert.Equal("Users2", n));

        // Extra safety: ensure all distinct (guards against future logic regressions)
        Assert.Equal(finalNames.Count, new HashSet<string>(finalNames, StringComparer.OrdinalIgnoreCase).Count);
    }
}
