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
            // Prevent base DbContext from attempting real SQL queries in unit tests
            return Task.FromResult(new List<T>());
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

    // (All tests removed per request)

}
