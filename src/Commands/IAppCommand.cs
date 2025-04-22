using System.Threading.Tasks;

namespace SpocR.Commands;

public interface IAppCommand
{
    Task<int> OnExecuteAsync();
}
