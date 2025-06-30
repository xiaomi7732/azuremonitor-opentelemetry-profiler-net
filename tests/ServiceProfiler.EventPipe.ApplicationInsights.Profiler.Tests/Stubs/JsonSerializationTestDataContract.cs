namespace ServiceProfiler.EventPipe.Client.Tests
{
    class JsonSerializationTestDataContract
    {
        public string StringValue { get; set; } = null!;
        public JsonSerializationTestDataType DataType { get; set; } = JsonSerializationTestDataType.Unknown;
    }
}
