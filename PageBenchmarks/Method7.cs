﻿using System.Text.Json;

namespace PageBenchmarks;

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
        var result = PageMethod7("john", 625, 10);
    }
}
