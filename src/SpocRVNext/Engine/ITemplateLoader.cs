using System.Collections.Generic;

namespace SpocR.SpocRVNext.Engine;

/// <summary>
/// Abstraction for obtaining template content by logical name.
/// Keeps file system concerns separate from rendering logic so tests can inject in-memory templates.
/// </summary>
public interface ITemplateLoader
{
    /// <summary>
    /// Try to load one template by name (case-insensitive recommended by implementers).
    /// </summary>
    /// <param name="name">Logical template name (e.g. "DbContext").</param>
    /// <param name="content">Returned raw template content.</param>
    /// <returns>True if found.</returns>
    bool TryLoad(string name, out string content);

    /// <summary>
    /// Enumerates all available template logical names.
    /// </summary>
    IEnumerable<string> ListNames();
}
