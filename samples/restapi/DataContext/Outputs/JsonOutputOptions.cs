using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RestApi.DataContext.Outputs
{
    /// <summary>
    /// Wrapper combining the standard Output metadata with a typed JSON model result.
    /// </summary>
    /// <typeparam name="TModel">Deserialized JSON root model type.</typeparam>
    public class JsonOutputOptions<TModel> : IOutput where TModel : class
    {
        public JsonOutputOptions(Output output, TModel model)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            Output = output;
            Model = model;
        }

        public Output Output { get; }
        public TModel Model { get; }

        public int ResultId => Output.ResultId;
        public int? RecordId => Output.RecordId;
        public long? RowVersion => Output.RowVersion;
        public EOutputResult Result => Output.Result;
    }
}
