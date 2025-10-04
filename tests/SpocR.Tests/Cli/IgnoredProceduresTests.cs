using System;
using System.Collections.Generic;
using System.Linq;
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

public class IgnoredProceduresTests
{
    private class TestDbContext : DbContext
    {
        private readonly List<StoredProcedure> _sps;
        private readonly Dictionary<string, string> _defs;
        public TestDbContext(IConsoleService console, IEnumerable<StoredProcedure> sps, Dictionary<string, string> defs) : base(console)
        {
            _sps = sps.ToList();
            _defs = defs;
        }
        public Task<List<StoredProcedure>> StoredProcedureListAsync(string schemaList, System.Threading.CancellationToken ct) => Task.FromResult(_sps);
        public Task<StoredProcedureDefinition> StoredProcedureDefinitionAsync(string schema, string name, System.Threading.CancellationToken ct)
        {
            var key = $"{schema}.{name}";
            return Task.FromResult(new StoredProcedureDefinition { SchemaName = schema, Name = name, Definition = _defs.TryGetValue(key, out var d) ? d : null });
        }
        public Task<List<Schema>> SchemaListAsync(System.Threading.CancellationToken ct) => Task.FromResult(_sps.Select(s => s.SchemaName).Distinct().Select(n => new Schema { Name = n }).ToList());
        public Task<List<TableType>> TableTypeListAsync(string schemaList, System.Threading.CancellationToken ct) => Task.FromResult(new List<TableType>()); // none needed
        public Task<List<Column>> TableTypeColumnListAsync(int id, System.Threading.CancellationToken ct) => Task.FromResult(new List<Column>());
        protected override Task<List<T>> OnListAsync<T>(string qs, List<Microsoft.Data.SqlClient.SqlParameter> p, System.Threading.CancellationToken c, AppSqlTransaction t)
        {
            return base.OnListAsync<T>(qs, p, c, t);
        }
    }

    private static ConfigurationModel CreateConfig(IEnumerable<string> ignoredSchemas = null, IEnumerable<string> ignoredProcedures = null)
    {
        return new ConfigurationModel
        {
            TargetFramework = "net8.0",
            Project = new ProjectModel
            {
                DataBase = new DataBaseModel { ConnectionString = "Server=.;Database=Db;Trusted_Connection=True;" },
                Output = new OutputModel { Namespace = "Test.Namespace", DataContext = new DataContextModel { Path = "out" } },
                DefaultSchemaStatus = SchemaStatusEnum.Build,
                IgnoredSchemas = ignoredSchemas?.ToList() ?? new List<string>(),
                IgnoredProcedures = ignoredProcedures?.ToList() ?? new List<string>()
            }
        };
    }

    [Fact]
    public async Task IgnoredProcedures_Should_Filter_Only_Listed_Procedures()
    {
        // Arrange
        var console = new Mock<IConsoleService>();
        var procs = new List<StoredProcedure>
        {
            new StoredProcedure { SchemaName = "core", Name = "UserList", Modified = DateTime.UtcNow },
            new StoredProcedure { SchemaName = "core", Name = "UserFind", Modified = DateTime.UtcNow },
            new StoredProcedure { SchemaName = "audit", Name = "CleanupJob", Modified = DateTime.UtcNow },
            new StoredProcedure { SchemaName = "other", Name = "KeepAlive", Modified = DateTime.UtcNow }
        };
        var defs = procs.ToDictionary(p => $"{p.SchemaName}.{p.Name}", p => "CREATE PROCEDURE ... AS SELECT 1 FOR JSON PATH;");
        var db = new TestDbContext(console.Object, procs, defs);
        var cache = new Mock<ILocalCacheService>();
        var snapshot = new Mock<ISchemaSnapshotService>();

        var schemaManager = new SchemaManager(db, console.Object, snapshot.Object, cache.Object);
        var cfg = CreateConfig(ignoredSchemas: new[] { "audit" }, ignoredProcedures: new[] { "core.UserFind" });

        // Act
        var schemas = await schemaManager.ListAsync(cfg, noCache: true);

        // Assert
        // audit schema ignored -> only core + other remain
        schemas.Select(s => s.Name).Should().BeEquivalentTo(new[] { "core", "other" });
        var coreSchema = schemas.Single(s => s.Name == "core");
        coreSchema.StoredProcedures.Select(p => p.Name).Should().BeEquivalentTo(new[] { "UserList" }); // UserFind filtered
        var otherSchema = schemas.Single(s => s.Name == "other");
        otherSchema.StoredProcedures.Should().ContainSingle(p => p.Name == "KeepAlive");
    }
}
