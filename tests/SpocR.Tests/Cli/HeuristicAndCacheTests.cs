using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using SpocR.DataContext;
using SpocR.DataContext.Models;
using SpocR.Managers;
using SpocR.Models;
using SpocR.Services;
using Xunit;

namespace SpocR.Tests.Cli;

public class HeuristicAndCacheTests
{
    private class FakeLocalCacheService : ILocalCacheService
    {
        public ProcedureCacheSnapshot Loaded;
        public ProcedureCacheSnapshot Saved;
        public ProcedureCacheSnapshot Load(string fingerprint) => Loaded;
        public void Save(string fingerprint, ProcedureCacheSnapshot snapshot) => Saved = snapshot;
    }

    private class TestDbContext : DbContext
    {
        private readonly List<StoredProcedure> _storedProcedures;
        private readonly Dictionary<string, string> _definitions;
        private readonly Dictionary<string, List<StoredProcedureInput>> _inputs;
        private readonly Dictionary<string, List<StoredProcedureOutput>> _outputs;
        public int DefinitionCalls { get; private set; }
        public int InputCalls { get; private set; }
        public int OutputCalls { get; private set; }

        public TestDbContext(IConsoleService console, IEnumerable<StoredProcedure> sps, Dictionary<string, string> defs, Dictionary<string, List<StoredProcedureInput>> inputs, Dictionary<string, List<StoredProcedureOutput>> outputs) : base(console)
        {
            _storedProcedures = sps.ToList();
            _definitions = defs;
            _inputs = inputs;
            _outputs = outputs;
        }

        public Task<List<StoredProcedure>> StoredProcedureListAsync(string schemaList, CancellationToken ct) => Task.FromResult(_storedProcedures);

        public Task<StoredProcedureDefinition> StoredProcedureDefinitionAsync(string schema, string name, CancellationToken ct)
        {
            DefinitionCalls++;
            var key = $"{schema}.{name}";
            return Task.FromResult(new StoredProcedureDefinition { SchemaName = schema, Name = name, Definition = _definitions.TryGetValue(key, out var d) ? d : null });
        }

        public Task<List<StoredProcedureInput>> StoredProcedureInputListAsync(string schema, string name, CancellationToken ct)
        {
            InputCalls++;
            var key = $"{schema}.{name}";
            return Task.FromResult(_inputs.TryGetValue(key, out var l) ? l : new List<StoredProcedureInput>());
        }

        public Task<List<StoredProcedureOutput>> StoredProcedureOutputListAsync(string schema, string name, CancellationToken ct)
        {
            OutputCalls++;
            var key = $"{schema}.{name}";
            return Task.FromResult(_outputs.TryGetValue(key, out var l) ? l : new List<StoredProcedureOutput>());
        }

        // Minimal shims for other required calls in manager path
        public Task<List<TableType>> TableTypeListAsync(string schemaList, CancellationToken ct) => Task.FromResult(new List<TableType>());
        public Task<Column> TableColumnAsync(string schema, string table, string column, CancellationToken ct) => Task.FromResult<Column>(null);
        public Task<List<Schema>> SchemaListAsync(CancellationToken ct) => Task.FromResult(_storedProcedures.Select(s => s.SchemaName).Distinct().Select(n => new Schema { Name = n }).ToList());
        public Task<List<TableTypeColumn>> TableTypeColumnListAsync(int id, CancellationToken ct) => Task.FromResult(new List<TableTypeColumn>());
        public Task<DbObject> ObjectAsync(string schema, string name, CancellationToken ct) => Task.FromResult(new DbObject { Id = 1 });
    }

    private static IConsoleService SilentConsole()
    {
        var mock = new Mock<IConsoleService>();
        mock.Setup(x => x.IsQuiet).Returns(true);
        return mock.Object;
    }

    private static ConfigurationModel TestConfig(params string[] schemas) => new()
    {
        Project = new ProjectModel
        {
            Output = new ProjectOutputModel { Namespace = "Test.Namespace" },
            Role = new ProjectRoleModel { Kind = SpocR.Enums.RoleKindEnum.Default },
            DefaultSchemaStatus = SchemaStatusEnum.Build
        },
        Schema = schemas.Select(s => new SchemaModel { Name = s, Status = SchemaStatusEnum.Build }).ToList()
    };

    [Fact]
    public async Task Heuristic_AsJson_Name_Sets_ReturnsJson_When_No_ForJson()
    {
        var sp = new StoredProcedure { SchemaName = "dbo", Name = "GetUsersAsJson", Modified = DateTime.UtcNow };
        var ctx = new TestDbContext(SilentConsole(), new[] { sp }, new() { { "dbo.GetUsersAsJson", "SELECT 1" } }, new(), new());
        var cacheService = new FakeLocalCacheService();
        var manager = new SchemaManager(ctx, SilentConsole(), cacheService);
        var cfg = TestConfig("dbo");

        var schemas = await manager.ListAsync(cfg);
        var proc = schemas.SelectMany(s => s.StoredProcedures).Single();
        proc.ReturnsJson.Should().BeTrue("name ends with AsJson and definition lacks explicit FOR JSON but heuristic should trigger");
    }

    [Fact]
    public async Task Caching_Skips_Unchanged_Definition_Load()
    {
        var modified = DateTime.UtcNow;
        var sp = new StoredProcedure { SchemaName = "dbo", Name = "GetUsers", Modified = modified };
        var defs = new Dictionary<string, string> { { "dbo.GetUsers", "SELECT 1" } };
        var ctx = new TestDbContext(SilentConsole(), new[] { sp }, defs, new(), new());
        var cache = new FakeLocalCacheService();
        var manager = new SchemaManager(ctx, SilentConsole(), cache);
        var cfg = TestConfig("dbo");

        // First run populates cache
        var schemas1 = await manager.ListAsync(cfg);
        ctx.DefinitionCalls.Should().Be(1);
        cache.Saved.Procedures.Should().ContainSingle();

        // Prepare second run with loaded cache snapshot
        cache.Loaded = cache.Saved; // unchanged modify_date
        var ctx2 = new TestDbContext(SilentConsole(), new[] { sp }, defs, new(), new());
        var manager2 = new SchemaManager(ctx2, SilentConsole(), cache);
        var schemas2 = await manager2.ListAsync(cfg);
        ctx2.DefinitionCalls.Should().Be(0, "definition should be skipped when modify_date unchanged");
    }
}
