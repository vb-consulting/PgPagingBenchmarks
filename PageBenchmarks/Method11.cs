using System.Text.Json;

namespace PageBenchmarks;

static partial class Functions
{
    public static void RecreatePageMethod11()
    {
        using var connection = new NpgsqlConnection(PageTest.ConnectionStr);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
        create or replace function example.method11(
            _search varchar, 
            _skip integer, 
            _take integer
        ) 
        returns table (
            _customer_id int,
            _name text,
            _address_id int,
            _street text,
            _city_id int,
            _city_name text,
            _address_count bigint,
            _count bigint
        )
        language sql 
        /*stable*/
        as $$
        with cte as /*materialized*/ (
            select customer_id 
            from example.customers 
            where name ilike _search
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
        offset _skip limit _take
        $$;
        """;
        command.ExecuteNonQuery();
    }
}

public partial class PageTest
{
    private DataPage PageMethod11(string search, int page, int pageSize)
    {
        using var connection = new NpgsqlConnection(ConnectionStr);
        connection.Open();

        using var command = connection.CreateCommand();
        command.Parameters.Add(new NpgsqlParameter() { Value = string.Concat("%", search, "%"), NpgsqlDbType = NpgsqlDbType.Text });
        command.Parameters.Add(new NpgsqlParameter() { Value = page * pageSize, NpgsqlDbType = NpgsqlDbType.Integer });
        command.Parameters.Add(new NpgsqlParameter() { Value = pageSize, NpgsqlDbType = NpgsqlDbType.Integer });

        command.CommandText = "select _customer_id, _name, _address_id, _street, _city_id, _city_name, _address_count, _count from example.method11($1, $2, $3)";

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
    public void Method11()
    {
        var result = PageMethod11("john", 871, 10);
    }
}
