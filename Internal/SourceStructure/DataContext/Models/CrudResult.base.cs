using Newtonsoft.Json;

namespace Source.DataContext.Models
{
    public class CrudResult : ICrudResult
    {
        private bool? _succeeded;
        private bool? _modified;

        public CrudResult()
        {
            // require parameterless constructor
        }

        public CrudResult(int resultId, int? recordId = null)
        {
            ResultId = resultId;
            RecordId = recordId;
        }

        public CrudResult(bool succeeded, bool? modified = false)
        {
            _succeeded = succeeded;
            _modified = modified;
        }
        
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Succeeded => _succeeded ?? ((_succeeded = ResultId == 1) ?? false);

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Modified => _modified ?? ((_modified = ResultId == -10) ?? false);

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? ResultId { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? RecordId { get; set; }
    }

    public interface ICrudResult
    {
        int? ResultId { get; }

        int? RecordId { get; }
    }
}