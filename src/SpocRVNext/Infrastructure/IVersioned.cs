using System;

namespace SpocR.SpocRVNext.Infrastructure;

public interface IVersioned
{
    Version Version { get; set; }
}
