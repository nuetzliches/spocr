using SpocR.Commands.Spocr;
using SpocR.Managers;

namespace SpocR.Commands.Schema;

public class SchemaCommandBase(
    SpocrProjectManager spocrProjectManager
) : SpocrCommandBase(spocrProjectManager)
{
}
