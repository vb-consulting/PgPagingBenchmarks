using System.Text.Json;

namespace PageBenchmarks;

public partial class PageTest
{
    public const string ConnectionStr = "Host=localhost; Port=5436; Username=postgres; Password=postgres; Database=example; Pooling=false";
    private static readonly JsonSerializerOptions options  = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    [GlobalSetup]
    public void Setup()
    {
    }

    [GlobalCleanup]
    public void Cleanup()
    {
    }

    private static Customer GetCustomerFromReaderByposition(NpgsqlDataReader dataReader)
    {
        return new Customer
        {
            Id = dataReader.GetInt32(0),
            Name = dataReader.GetString(1),
            Address = new Address
            {
                Id = dataReader.GetInt32(2),
                Street = dataReader.GetString(3),
                City = new City
                {
                    Id = dataReader.GetInt32(4),
                    Name = dataReader.GetString(5)
                }
            },
            AddressCount = dataReader.GetInt64(6)
        };
    }
}
