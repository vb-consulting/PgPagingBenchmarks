namespace PageBenchmarks;

public partial class PageTest
{
    private DataPage PageMethod5(string search, int page, int pageSize)
    {
        using var connection = new NpgsqlConnection(ConnectionStr);
        connection.Open();

        using var command = connection.CreateCommand();
        command.Parameters.Add(new NpgsqlParameter() { Value = string.Concat("%", search, "%"), NpgsqlDbType = NpgsqlDbType.Text });
        command.Parameters.Add(new NpgsqlParameter() { Value = page * pageSize, NpgsqlDbType = NpgsqlDbType.Integer });
        command.Parameters.Add(new NpgsqlParameter() { Value = pageSize, NpgsqlDbType = NpgsqlDbType.Integer });

        command.CommandText = "begin";
        command.ExecuteNonQuery();
        
        command.CommandText = """
        create temp table _temp_customers on commit drop as
        select row_number() over() as row, customer_id 
        from example.customers 
        where name ilike $1
        order by customers.name
        """;
        command.ExecuteNonQuery();

        command.CommandText = "select max(row) from _temp_customers";
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
            _temp_customers
            join example.customers using (customer_id)
            join example.addresses using (address_id)
            join example.cities using (city_id)
        where row > $2 and row <= $2 + $3
        group by
            row,
            customers.customer_id,
            customers.name,
            customers.address_id,
            street,
            cities.city_id,
            cities.name
        order by row
        """;
        var customers = new List<Customer>();
        using var dataReader = command.ExecuteReader();
        while (dataReader.Read())
        {
            customers.Add(GetCustomerFromReaderByposition(dataReader));
        }
        dataReader.Close();

        command.CommandText = "end";
        command.ExecuteNonQuery();

        return new DataPage
        {
            Count = count,
            Customers = customers
        };
    }

    [Benchmark]
    public void Method5()
    {
        var result = PageMethod5("john", 871, 10);
    }
}
