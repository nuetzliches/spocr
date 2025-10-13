using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using SpocR.SpocRVNext.Execution;
using Xunit;

namespace SpocR.Tests.SpocRVNext.Execution;

public class ProcedureExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_CapturesOutputParameters()
    {
        var fake = new FakeDb();
        var plan = new ProcedureExecutionPlan(
            "dbo.TestProc",
            new[] { new ProcedureParameter("@x", DbType.Int32, null, false, false), new ProcedureParameter("@y", DbType.Int32, null, true, false) },
            Array.Empty<ResultSetMapping>(),
            outputFactory: values => new Out(values.ContainsKey("y") ? (int?)values["y"] : null),
            aggregateFactory: (success, error, output, outputs, rs) => new Agg { Success = success, Error = error, Output = (Out?)output },
            inputBinder: (cmd, state) => { cmd.Parameters["@x"].Value = 5; cmd.Parameters["@y"].Value = 0; }
        );
        fake.SetExecuteBehavior(cmd => { cmd.Parameters["@y"].Value = 42; });

        var result = await ProcedureExecutor.ExecuteAsync<Agg>(fake.Connection, plan, null, CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal(42, result.Output?.Y);
    }

    [Fact]
    public async Task ExecuteAsync_OnException_FailsAndSetsError()
    {
        var fake = new FakeDb();
        var plan = new ProcedureExecutionPlan(
            "dbo.TestProc",
            Array.Empty<ProcedureParameter>(),
            Array.Empty<ResultSetMapping>(),
            outputFactory: values => null,
            aggregateFactory: (success, error, output, outputs, rs) => new Agg { Success = success, Error = error }
        );
        fake.SetExecuteBehavior(_ => throw new InvalidOperationException("boom"));
        var result = await ProcedureExecutor.ExecuteAsync<Agg>(fake.Connection, plan, null, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("boom", result.Error);
    }

    private sealed class Agg
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public Out? Output { get; set; }
    }
    private sealed record Out(int? Y);

    #region Fakes
    private sealed class FakeDb
    {
        public FakeConnection Connection { get; } = new();
        public void SetExecuteBehavior(Action<DbCommand> behavior) => Connection.ExecuteBehavior = behavior;
    }

    private sealed class FakeConnection : DbConnection
    {
        private ConnectionState _state = ConnectionState.Closed;
        public Action<DbCommand>? ExecuteBehavior { get; set; }
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "Fake";
        public override string DataSource => "Fake";
        public override string ServerVersion => "1";
        public override ConnectionState State => _state;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() => _state = ConnectionState.Closed;
        public override void Open() => _state = ConnectionState.Open;
        public override Task OpenAsync(CancellationToken cancellationToken) { _state = ConnectionState.Open; return Task.CompletedTask; }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new FakeCommand { Connection = this, ExecuteBehavior = ExecuteBehavior };
    }

    private sealed class FakeCommand : DbCommand
    {
        public Action<DbCommand>? ExecuteBehavior { get; set; }
        private readonly FakeParameterCollection _parameters = new();
        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection => _parameters;
        protected override DbTransaction DbTransaction { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override void Cancel() { }
        public override int ExecuteNonQuery() { ExecuteBehavior?.Invoke(this); return 0; }
        public override object ExecuteScalar() { ExecuteBehavior?.Invoke(this); return 0; }
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new FakeParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) { ExecuteBehavior?.Invoke(this); return new EmptyReader(); }
        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken) { ExecuteBehavior?.Invoke(this); return Task.FromResult<DbDataReader>(new EmptyReader()); }
    }

    private sealed class FakeParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        public override string ParameterName { get; set; } = string.Empty;
        public override string SourceColumn { get; set; } = string.Empty;
        public override object? Value { get; set; }
        public override bool SourceColumnNullMapping { get; set; }
        public override int Size { get; set; }
        public override void ResetDbType() { }
    }

    private sealed class FakeParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _inner = new();
        public override int Add(object value) { _inner.Add((DbParameter)value); return _inner.Count - 1; }
        public override void AddRange(Array values) { foreach (var v in values) Add(v); }
        public override void Clear() => _inner.Clear();
        public override bool Contains(object value) => _inner.Contains((DbParameter)value);
        public override bool Contains(string value) => _inner.Exists(p => p.ParameterName == value);
        public override void CopyTo(Array array, int index) => _inner.ToArray().CopyTo(array, index);
        public override int Count => _inner.Count;
        public override System.Collections.IEnumerator GetEnumerator() => _inner.GetEnumerator();
        protected override DbParameter GetParameter(int index) => _inner[index];
        protected override DbParameter GetParameter(string parameterName) => _inner.Find(p => p.ParameterName == parameterName)!;
        public override int IndexOf(object value) => _inner.IndexOf((DbParameter)value);
        public override int IndexOf(string parameterName) => _inner.FindIndex(p => p.ParameterName == parameterName);
        public override void Insert(int index, object value) => _inner.Insert(index, (DbParameter)value);
        public override bool IsFixedSize => false;
        public override bool IsReadOnly => false;
        public override bool IsSynchronized => false;
        public override void Remove(object value) => _inner.Remove((DbParameter)value);
        public override void RemoveAt(int index) => _inner.RemoveAt(index);
        public override void RemoveAt(string parameterName) { var i = IndexOf(parameterName); if (i >= 0) _inner.RemoveAt(i); }
        protected override void SetParameter(int index, DbParameter value) => _inner[index] = value;
        protected override void SetParameter(string parameterName, DbParameter value) { var i = IndexOf(parameterName); if (i >= 0) _inner[i] = value; }
        public override object SyncRoot => this;
    }

    private sealed class EmptyReader : DbDataReader
    {
        public override int FieldCount => 0;
        public override bool HasRows => false;
        public override bool IsClosed => false;
        public override int RecordsAffected => 0;
        public override int Depth => 0;
        public override System.Collections.IEnumerator GetEnumerator() { yield break; }
        public override bool GetBoolean(int ordinal) => throw new NotSupportedException();
        public override byte GetByte(int ordinal) => throw new NotSupportedException();
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override char GetChar(int ordinal) => throw new NotSupportedException();
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override string GetDataTypeName(int ordinal) => string.Empty;
        public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();
        public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();
        public override double GetDouble(int ordinal) => throw new NotSupportedException();
        public override Type GetFieldType(int ordinal) => typeof(object);
        public override float GetFloat(int ordinal) => throw new NotSupportedException();
        public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
        public override short GetInt16(int ordinal) => throw new NotSupportedException();
        public override int GetInt32(int ordinal) => throw new NotSupportedException();
        public override long GetInt64(int ordinal) => throw new NotSupportedException();
        public override string GetName(int ordinal) => string.Empty;
        public override int GetOrdinal(string name) => -1;
        public override string GetString(int ordinal) => string.Empty;
        public override object GetValue(int ordinal) => DBNull.Value;
        public override int GetValues(object[] values) => 0;
        public override bool IsDBNull(int ordinal) => true;
        public override bool NextResult() => false;
        public override Task<bool> NextResultAsync(CancellationToken cancellationToken) => Task.FromResult(false);
        public override bool Read() => false;
        public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(false);
        public override object this[string name] => DBNull.Value;
        public override object this[int ordinal] => DBNull.Value;
    }
    #endregion
}
