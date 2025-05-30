﻿using System;
using System.Text.Json.Serialization;

namespace Source.DataContext.Models
{
    //[Obsolete("This CrudResult will be removed in vNext. Please migrate StoredProcedures to OUTPUT-Pattern (e.g. @ResultId [core].[_id] OUTPUT)")]
    public class CrudResult : ICrudResult
    {
        private bool? _succeeded;
        private bool? _modified;
        private bool? _hasDependencies;
        private bool? _alreadyExists;

        public CrudResult()
        {
            // require parameterless constructor
        }

        public CrudResult(int resultId, int? recordId = null, long? rowVersion = null)
        {
            ResultId = resultId;
            RecordId = recordId;
            RowVersion = rowVersion;
        }

        public CrudResult(bool succeeded, bool? modified = false)
        {
            _succeeded = succeeded;
            _modified = modified;
        }

        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public bool Succeeded => _succeeded ?? (_succeeded = ResultId > 0) ?? false;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Modified => _modified ?? (_modified = ResultId == -10) ?? false;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool HasDependencies => _hasDependencies ?? (_hasDependencies = ResultId == -11) ?? false;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool AlreadyExists => _alreadyExists ?? (_alreadyExists = ResultId == -12) ?? false;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? ResultId { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? RecordId { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? RowVersion { get; set; }
    }

    public interface ICrudResult
    {
        bool Succeeded { get; }
        int? ResultId { get; }
        int? RecordId { get; }
        long? RowVersion { get; }
    }
}
