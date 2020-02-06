using System;

namespace SpocR.Interfaces
{
    public interface IVersioned
    {
        Version Version { get; set; }
    }
}