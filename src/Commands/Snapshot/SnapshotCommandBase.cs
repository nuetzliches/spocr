using SpocR.Commands.Spocr;
using SpocR.Managers;

namespace SpocR.Commands.Snapshot;

public class SnapshotCommandBase(
    SpocrProjectManager spocrProjectManager
) : SpocrCommandBase(spocrProjectManager)
{
}
