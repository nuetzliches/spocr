using System.Text.Json.Serialization;

namespace Source.DataContext.Outputs
{
    public class Output : IOutput
    {
        public Output()
        {
            // require parameterless constructor
        }

        public Output(int resultId, int? recordId = null, long? rowVersion = null)
        {
            ResultId = resultId;
            RecordId = recordId;
            RowVersion = rowVersion;
        }

        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int ResultId { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? RecordId { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? RowVersion { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public EOutputResult Result => (EOutputResult)ResultId;
    }

    public interface IOutput
    {
        int ResultId { get; }
        int? RecordId { get; }
        long? RowVersion { get; }
        EOutputResult Result { get; }
    }

    public enum EOutputResult
    {
        Undefined = 0,
        Succeeded = 1,
        Aborted = -1,
        Modified = -10,
        HasDependencies = -11,
        AlreadyExists = -12,
        SqlException = -99
    }
}
