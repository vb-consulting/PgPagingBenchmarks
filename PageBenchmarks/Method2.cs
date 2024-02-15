namespace PageBenchmarks;

public partial class PageTest
{
    private DataPage PageMethod2(string search, int page, int pageSize)
    {
        using var connection = new NpgsqlConnection(ConnectionStr);
        connection.Open();

        using var command = connection.CreateCommand();
        command.Parameters.Add(new NpgsqlParameter() { Value = string.Concat("%", search, "%"), NpgsqlDbType = NpgsqlDbType.Text });
        command.Parameters.Add(new NpgsqlParameter() { Value = page * pageSize, NpgsqlDbType = NpgsqlDbType.Integer });
        command.Parameters.Add(new NpgsqlParameter() { Value = pageSize, NpgsqlDbType = NpgsqlDbType.Integer });
        command.CommandText = """
        with cte as materialized (
            select customer_id 
            from example.customers 
            where name ilike $1
        )
        select 
            customers.customer_id,
            customers.name,
            customers.address_id,
            street,
            cities.city_id,
            cities.name,
            count(*) as address_count,
            (select count(*) from cte) as count
        from 
            cte
            join example.customers using (customer_id)
            join example.addresses using (address_id)
            join example.cities using (city_id) 
            join example.customer_addresses using (customer_id)
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
        long? count = null;
        var customers = new List<Customer>();
        using var dataReader = command.ExecuteReader();
        while (dataReader.Read())
        {
            customers.Add(GetCustomerFromReaderByposition(dataReader));
            count ??= dataReader.GetInt64(7);
        }

        return new DataPage
        {
            Count = count ?? 0,
            Customers = customers
        };
    }

    [Benchmark]
    public void Method2()
    {
        var result = PageMethod2("john", 625, 10);
    }
}
