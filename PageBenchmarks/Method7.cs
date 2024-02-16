using System.Text.Json;

namespace PageBenchmarks;

static partial class Functions
{
    public static void RecreatePageMethod7()
    {
        using var connection = new NpgsqlConnection(PageTest.ConnectionStr);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
        create or replace function example.method7(
            _search varchar, 
            _skip integer, 
            _take integer
        ) 
        returns json
        language plpgsql 
        as $$
        declare
            _count bigint;
        begin
            create temp table _temp_customers on commit drop as
            select customer_id 
            from example.customers 
            where name ilike _search;
            
            get diagnostics _count = row_count;
            
            return json_build_object(
                'count', _count,
                'customers', (
                    select json_agg(sub)
                    from ( 
                    
                        select 
                            customers.customer_id as id,
                            customers.name,
                            json_build_object(
                                'id', customers.address_id,
                                'street', street,
                                'city', json_build_object(
                                    'id', cities.city_id,
                                    'name', cities.name
                                )
                            ) as address,
                            count(*) as AddressCount
                        from 
                            _temp_customers
                            join example.customers using (customer_id)
                            join example.addresses using (address_id)
                            join example.cities using (city_id)
                            join example.customer_addresses using (customer_id)
                        where customers.name ilike '%john%'
                        group by
                            customers.customer_id,
                            customers.name,
                            customers.address_id,
                            street,
                            cities.city_id,
                            cities.name
                        order by customers.name 
                        offset _skip limit _take

                    ) sub
                )

            );
        end
        $$;
        """;
        command.ExecuteNonQuery();
    }
}

public partial class PageTest
{
    private DataPage PageMethod7(string search, int page, int pageSize)
    {
        using var connection = new NpgsqlConnection(ConnectionStr);
        connection.Open();

        using var command = connection.CreateCommand();
        command.Parameters.Add(new NpgsqlParameter() { Value = string.Concat("%", search, "%"), NpgsqlDbType = NpgsqlDbType.Text });
        command.Parameters.Add(new NpgsqlParameter() { Value = page * pageSize, NpgsqlDbType = NpgsqlDbType.Integer });
        command.Parameters.Add(new NpgsqlParameter() { Value = pageSize, NpgsqlDbType = NpgsqlDbType.Integer });

        command.CommandText = "select example.method7($1, $2, $3)";
        using var reader = command.ExecuteReader();
        reader.Read();
        var json = reader.GetString(0);
        reader.Close();

        return JsonSerializer.Deserialize<DataPage>(json, options) ?? new DataPage();
    }

    [Benchmark]
    public void Method7()
    {
        var result = PageMethod7("john", 871, 10);
    }
}
