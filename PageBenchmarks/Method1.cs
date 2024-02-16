namespace PageBenchmarks;

public partial class PageTest
{
    private DataPage PageMethod1(string search, int page, int pageSize)
    {
        using var connection = new NpgsqlConnection(ConnectionStr);
        connection.Open();

        using var command = connection.CreateCommand();
        command.Parameters.Add(new NpgsqlParameter() { Value = string.Concat("%", search, "%"), NpgsqlDbType = NpgsqlDbType.Text });
        command.Parameters.Add(new NpgsqlParameter() { Value = page * pageSize, NpgsqlDbType = NpgsqlDbType.Integer });
        command.Parameters.Add(new NpgsqlParameter() { Value = pageSize, NpgsqlDbType = NpgsqlDbType.Integer });
        
        command.CommandText = "select count(*) from example.customers where name ilike $1";
        using var countReader = command.ExecuteReader();
        countReader.Read();
        var count = countReader.GetInt64(0);
        countReader.Close();

        command.CommandText = """
        select 
            customers.customer_id,
            customers.name,
            customers.address_id,
            street,
            cities.city_id,
            cities.name,
            count(*) as address_count
        from 
            example.customers
            join example.addresses using (address_id)
            join example.cities using (city_id)
            join example.customer_addresses using (customer_id)
        where customers.name ilike $1
        group by
            customers.customer_id,
            customers.name,
            customers.address_id,
            street,
            cities.city_id,
            cities.name
        order by customers.name 
        offset $2 limit $3 
        """;
        var customers = new List<Customer>();
        using var dataReader = command.ExecuteReader();
        while (dataReader.Read())
        {
            customers.Add(GetCustomerFromReaderByposition(dataReader));
        }

        return new DataPage
        {
            Count = count,
            Customers = customers
        };
    }

    [Benchmark(Baseline = true)]
    public void Method1()
    {
        var result = PageMethod1("john", 871, 10);
    }
}
