using Newtonsoft.Json;

namespace Source.DataContext.Models
{
    public class CrudResult : ICrudResult
    {
        private bool? _succeeded;
        private bool? _modified;
        private bool? _hasDependencies;

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
        
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
        public bool Succeeded => _succeeded ?? (_succeeded = ResultId > 0) ?? false;

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Modified => _modified ?? (_modified = ResultId == -10) ?? false;

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool HasDependencies => _hasDependencies ?? (_hasDependencies = ResultId == -2) ?? false;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? ResultId { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? RecordId { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
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