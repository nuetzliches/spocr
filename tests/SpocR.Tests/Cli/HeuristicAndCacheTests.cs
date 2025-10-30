using Microsoft.Data.SqlClient;
using System.Globalization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using Moq;
using SpocR.SpocRVNext.Data;
using SpocR.SpocRVNext.Data.Models;
using SpocR.SpocRVNext.Models;
using SpocR.SpocRVNext.Configuration;
using SpocR.SpocRVNext.Services;
using Xunit;
using SchemaManager = SpocR.SpocRVNext.Schema.SchemaManager;
using DbSchema = SpocR.SpocRVNext.Data.Models.Schema;

namespace SpocR.Tests.Cli;

public class HeuristicAndCacheTests
{
    private class FakeLocalCacheService : ILocalCacheService
    {
        public ProcedureCacheSnapshot Loaded { get; set; } = null!;
        public ProcedureCacheSnapshot Saved { get; set; } = null!;
        public ProcedureCacheSnapshot Load(string fingerprint) => Loaded;
        public void Save(string fingerprint, ProcedureCacheSnapshot snapshot) => Saved = snapshot;
    }

    private class TestDbContext : DbContext
    {
        private readonly List<StoredProcedure> _storedProcedures;
        private readonly Dictionary<string, string> _definitions;
        private readonly Dictionary<string, List<StoredProcedureInput>> _inputs;
        private readonly Dictionary<string, List<StoredProcedureOutput>> _outputs;
        private readonly Dictionary<string, int> _objectIds;
        private readonly Dictionary<int, string> _objectLookup;
        public int DefinitionCalls { get; private set; }
        public int InputCalls { get; private set; }
        public int OutputCalls { get; private set; }

        public TestDbContext(IConsoleService console, IEnumerable<StoredProcedure> sps, Dictionary<string, string> defs, Dictionary<string, List<StoredProcedureInput>> inputs, Dictionary<string, List<StoredProcedureOutput>> outputs) : base(console)
        {
            _storedProcedures = sps.ToList();
            _definitions = defs;
            _inputs = inputs;
            _outputs = outputs;

            _objectIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _objectLookup = new Dictionary<int, string>();
            var nextId = 100;
            foreach (var sp in _storedProcedures)
            {
                var key = $"{sp.SchemaName}.{sp.Name}";
                var id = nextId++;
                _objectIds[key] = id;
                _objectLookup[id] = key;
            }
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

        protected override Task<List<T>?> OnListAsync<T>(string queryString, List<SqlParameter> parameters, CancellationToken cancellationToken, AppSqlTransaction? transaction)
        {
            var normalized = queryString?.ToLowerInvariant() ?? string.Empty;

            if (typeof(T) == typeof(DbSchema) && normalized.Contains("from sys.schemas"))
            {
                var schemas = _storedProcedures
                    .Select(sp => sp.SchemaName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(name => (T)(object)new DbSchema { Name = name })
                    .ToList();
                return Task.FromResult<List<T>?>(schemas);
            }

            if (typeof(T) == typeof(StoredProcedure) && normalized.Contains("from sys.objects") && normalized.Contains("where o.type = n'p'"))
            {
                var list = _storedProcedures
                    .Select(sp => (T)(object)new StoredProcedure { SchemaName = sp.SchemaName, Name = sp.Name, Modified = sp.Modified })
                    .ToList();
                return Task.FromResult<List<T>?>(list);
            }

            if (typeof(T) == typeof(DbObject) && normalized.Contains("from sys.objects") && normalized.Contains("object_id"))
            {
                var key = GetSchemaKey(parameters);
                if (!string.IsNullOrEmpty(key) && _objectIds.TryGetValue(key, out var id))
                {
                    return Task.FromResult<List<T>?>(new List<T> { (T)(object)new DbObject { Id = id } });
                }
                return Task.FromResult<List<T>?>(new List<T>());
            }

            if (typeof(T) == typeof(StoredProcedureDefinition) && normalized.Contains("sys.sql_modules"))
            {
                var objectId = GetObjectId(parameters);
                if (objectId.HasValue && _objectLookup.TryGetValue(objectId.Value, out var key) && _definitions.TryGetValue(key, out var definition))
                {
                    DefinitionCalls++;
                    var parts = key.Split('.', 2);
                    var def = new StoredProcedureDefinition { SchemaName = parts[0], Name = parts[1], Id = objectId.Value, Definition = definition };
                    return Task.FromResult<List<T>?>(new List<T> { (T)(object)def });
                }
                return Task.FromResult<List<T>?>(new List<T>());
            }

            if (typeof(T) == typeof(StoredProcedureOutput) && normalized.Contains("dm_exec_describe_first_result_set_for_object"))
            {
                var objectId = GetObjectId(parameters);
                if (objectId.HasValue && _objectLookup.TryGetValue(objectId.Value, out var key) && _outputs.TryGetValue(key, out var outputs))
                {
                    var clones = outputs.Select(o => (T)(object)new StoredProcedureOutput
                    {
                        Name = o.Name,
                        IsNullable = o.IsNullable,
                        SqlTypeName = o.SqlTypeName,
                        MaxLength = o.MaxLength,
                        IsIdentityColumn = o.IsIdentityColumn
                    }).ToList();
                    return Task.FromResult<List<T>?>(clones);
                }
                return Task.FromResult<List<T>?>(new List<T>());
            }

            if (typeof(T) == typeof(StoredProcedureInput) && normalized.Contains("sys.parameters"))
            {
                var objectId = GetObjectId(parameters);
                if (objectId.HasValue && _objectLookup.TryGetValue(objectId.Value, out var key) && _inputs.TryGetValue(key, out var inputs))
                {
                    var clones = inputs.Select(i => (T)(object)new StoredProcedureInput
                    {
                        Name = i.Name,
                        IsNullable = i.IsNullable,
                        SqlTypeName = i.SqlTypeName,
                        MaxLength = i.MaxLength,
                        IsOutput = i.IsOutput,
                        IsTableType = i.IsTableType,
                        UserTypeName = i.UserTypeName,
                        UserTypeId = i.UserTypeId,
                        UserTypeSchemaName = i.UserTypeSchemaName,
                        TableTypeColumns = i.TableTypeColumns
                    }).ToList();
                    return Task.FromResult<List<T>?>(clones);
                }
                return Task.FromResult<List<T>?>(new List<T>());
            }

            if (typeof(T) == typeof(TableType) && normalized.Contains("from sys.table_types"))
            {
                return Task.FromResult<List<T>?>(new List<T>());
            }

            if (typeof(T) == typeof(Column) && normalized.Contains("from sys.table_types") && normalized.Contains("sys.columns"))
            {
                return Task.FromResult<List<T>?>(new List<T>());
            }

            if (typeof(T) == typeof(StoredProcedureContent) && normalized.Contains("select definition"))
            {
                var objectId = GetObjectId(parameters);
                if (objectId.HasValue && _objectLookup.TryGetValue(objectId.Value, out var key) && _definitions.TryGetValue(key, out var definition))
                {
                    var content = new StoredProcedureContent { Definition = definition };
                    return Task.FromResult<List<T>?>(new List<T> { (T)(object)content });
                }
                return Task.FromResult<List<T>?>(new List<T>());
            }

            return base.OnListAsync<T>(queryString, parameters, cancellationToken, transaction);
        }

        private static string? GetStringParameter(IEnumerable<SqlParameter> parameters, string name)
        {
            return parameters?.FirstOrDefault(p => string.Equals(p.ParameterName, name, StringComparison.OrdinalIgnoreCase))?.Value?.ToString();
        }

        private static string? GetSchemaKey(IEnumerable<SqlParameter> parameters)
        {
            var schema = GetStringParameter(parameters, "@schemaName");
            var name = GetStringParameter(parameters, "@name");
            return string.IsNullOrWhiteSpace(schema) || string.IsNullOrWhiteSpace(name) ? null : $"{schema}.{name}";
        }

        private static int? GetObjectId(IEnumerable<SqlParameter> parameters)
        {
            var value = parameters?.FirstOrDefault(p => string.Equals(p.ParameterName, "@objectId", StringComparison.OrdinalIgnoreCase))?.Value;
            if (value == null)
            {
                return null;
            }

            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        // Minimal shims for other required calls in manager path
        public Task<List<TableType>> TableTypeListAsync(string schemaList, CancellationToken ct) => Task.FromResult(new List<TableType>());
        public Task<Column?> TableColumnAsync(string schema, string table, string column, CancellationToken ct) => Task.FromResult<Column?>(null);
        public Task<List<DbSchema>> SchemaListAsync(CancellationToken ct) => Task.FromResult(_storedProcedures.Select(s => s.SchemaName).Distinct().Select(n => new DbSchema { Name = n }).ToList());
        public Task<List<Column>> TableTypeColumnListAsync(int id, CancellationToken ct) => Task.FromResult(new List<Column>());
        public Task<DbObject?> ObjectAsync(string schema, string name, CancellationToken ct)
        {
            var key = $"{schema}.{name}";
            return Task.FromResult(_objectIds.TryGetValue(key, out var id) ? new DbObject { Id = id } : null);
        }
    }

    private static IConsoleService SilentConsole()
    {
        var mock = new Mock<IConsoleService>();
        return mock.Object;
    }

    private static ConfigurationModel TestConfig(params string[] schemas) => new()
    {
        Project = new ProjectModel
        {
            Output = new OutputModel
            {
                Namespace = "Test.Namespace",
                DataContext = new DataContextModel
                {
                    Path = "./data",
                    Inputs = new DataContextInputsModel { Path = "./inputs" },
                    Outputs = new DataContextOutputsModel { Path = "./outputs" },
                    Models = new DataContextModelsModel { Path = "./models" },
                    StoredProcedures = new DataContextStoredProceduresModel { Path = "./stored" },
                    TableTypes = new DataContextTableTypesModel { Path = "./tables" },
                }
            },
            Role = new RoleModel { Kind = RoleKindEnum.Default },
            DefaultSchemaStatus = SchemaStatusEnum.Build
        },
        Schema = schemas.Select(s => new SchemaModel { Name = s, Status = SchemaStatusEnum.Build }).ToList()
    };

    // Removed heuristic test: AsJson suffix no longer triggers JSON detection without explicit FOR JSON (placeholder comment to force rebuild)

    [Fact]
    public async Task Caching_Skips_Unchanged_Definition_Load()
    {
        var modified = DateTime.UtcNow;
        var sp = new StoredProcedure { SchemaName = "dbo", Name = "GetUsers", Modified = modified };
        var defs = new Dictionary<string, string> { { "dbo.GetUsers", "SELECT 1" } };
        var ctx = new TestDbContext(SilentConsole(), new[] { sp }, defs, new(), new());
        var cache = new FakeLocalCacheService();
        var manager = new SchemaManager(ctx, SilentConsole(), new FakeSchemaSnapshotService(), new SpocR.SpocRVNext.Services.SchemaSnapshotFileLayoutService(), cache);
        var cfg = TestConfig("dbo");

        // First run populates cache
        var schemas1 = await manager.ListAsync(cfg);
        ctx.DefinitionCalls.ShouldBe(1);
        cache.Saved.Procedures.Count.ShouldBe(1);

        // Prepare second run with loaded cache snapshot
        cache.Loaded = cache.Saved; // unchanged modify_date
        var ctx2 = new TestDbContext(SilentConsole(), new[] { sp }, defs, new(), new());
        var manager2 = new SchemaManager(ctx2, SilentConsole(), new FakeSchemaSnapshotService(), new SpocR.SpocRVNext.Services.SchemaSnapshotFileLayoutService(), cache);
        var schemas2 = await manager2.ListAsync(cfg);
        ctx2.DefinitionCalls.ShouldBe(0, "definition should be skipped when modify_date unchanged");
    }
}

internal class FakeSchemaSnapshotService : ISchemaSnapshotService
{
    public SchemaSnapshot Load(string fingerprint) => null!; // test stub
    public void Save(SchemaSnapshot snapshot) { }
    public string BuildFingerprint(string serverName, string databaseName, IEnumerable<string> includedSchemas, int procedureCount, int udttCount, int parserVersion) => "test";
}
