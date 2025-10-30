using System;
using SpocR.SpocRVNext.Services; // for HashUtils

namespace SpocR.SpocRVNext.Metadata
{
    /// <summary>
    /// Provides hashing and truncation logic for function definitions (OBJECT_DEFINITION).
    /// - Computes SHA256 hex hash of the original definition (empty string when null)
    /// - Marks encrypted definitions (definition == null)
    /// - Truncates definitions longer than MaxLength and appends a deterministic suffix
    /// </summary>
    internal static class FunctionDefinitionProcessor
    {
        public const int MaxLength = 4000; // snapshot stored definition soft limit
        private const string TruncatedSuffix = "/*+TRUNCATED*/";

        /// <summary>
        /// Processes a function body definition returning possibly truncated text and outputs hash & encryption flag.
        /// </summary>
        /// <param name="definition">Raw T-SQL from OBJECT_DEFINITION(fn.object_id) or null if encrypted.</param>
        /// <param name="hash">SHA256 HEX (uppercase) of original definition (empty string when null).</param>
        /// <param name="isEncrypted">True when OBJECT_DEFINITION returned null (encrypted or inaccessible).</param>
        /// <returns>Possibly truncated definition ("" when encrypted).</returns>
        public static string Process(string? definition, out string hash, out bool isEncrypted)
        {
            if (definition == null)
            {
                isEncrypted = true;
                hash = HashUtils.Sha256Hex(string.Empty);
                return string.Empty; // do not fabricate placeholder
            }
            isEncrypted = false;
            hash = HashUtils.Sha256Hex(definition);
            if (definition.Length <= MaxLength)
                return definition;
            // Truncate deterministically
            var sliceLen = MaxLength - TruncatedSuffix.Length;
            if (sliceLen < 0) sliceLen = 0; // defensive
            var truncated = definition.Substring(0, sliceLen) + TruncatedSuffix;
            return truncated;
        }
    }
}
