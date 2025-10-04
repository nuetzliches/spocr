using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SpocR.CodeGenerators.Base;
using SpocR.CodeGenerators.Utils;
using SpocR.Contracts;
using SpocR.Enums;
using SpocR.Extensions;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Roslyn.Helpers;
using SpocR.Services;
using SpocR.Utils;

namespace SpocR.CodeGenerators.Models;

// Legacy OutputGenerator removed after migration to unified ResultSets.
public class OutputGenerator(
    FileManager<ConfigurationModel> configFile,
    OutputService output,
    IConsoleService consoleService
) : GeneratorBase(configFile, output, consoleService)
{
    public Task GenerateDataContextOutputsAsync(bool isDryRun) => Task.CompletedTask; // no-op
}
