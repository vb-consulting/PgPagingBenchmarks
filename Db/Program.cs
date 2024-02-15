using Bogus;
using Npgsql;
using Norm;
using static System.Console;

const int numberOfCustomers = 100_000;
const int minAddresses = 1;
const int maxAddresses = 15;

/*
NormOptions.Configure(options =>
{
    options.CommandCommentHeader.Enabled = true;
    options.CommandCommentHeader.IncludeCallerInfo = false;
    options.CommandCommentHeader.IncludeCommandAttributes = false;
    options.CommandCommentHeader.IncludeTimestamp = false;
    options.CommandCommentHeader.IncludeParameters = true;
    options.DbCommandCallback = cmd => WriteLine(cmd.CommandText);
});
*/
NormOptions.Configure(options => 
{
    options.CommandTimeout = 60 * 60 * 24;
});

using var connection = new NpgsqlConnection("Host=localhost; Port=5436; Username=postgres; Password=postgres; Database=example");

//connection.Execute(File.ReadAllText("script.sql"));
//Line("Database recreated");

connection.Execute("begin");
connection.Execute("set constraints all deferred");

Randomizer.Seed = new Random(DateTime.Now.Millisecond);
var faker = new Faker();
int addrCount = 0;

foreach (var custIdx in Enumerable.Range(1, numberOfCustomers))
{
    var customer = faker.Company.CompanyName();
    Line($"Customer {custIdx}: {customer}");
    int? customerId = null;
    foreach (var addrIdx in Enumerable.Range(minAddresses, faker.Random.Int(minAddresses, maxAddresses)))
    {
        addrCount++;
        var addr = faker.Address;
        var cityId = connection
            .Read<int>(@"
                insert into example.cities (name) values ($1) 
                on conflict (name) do update set name = excluded.name
                returning city_id", addr.City())
            .Single();
        var addrId = connection
            .WithParameters(addr.StreetAddress(), cityId)
            .Read<int>(@"
                insert into example.addresses (street, city_id) values ($1, $2) 
                on conflict (street, city_id) do update set street = excluded.street, city_id = excluded.city_id
                returning address_id")
            .Single();

        if (addrIdx == 1)
        {
            customerId = connection
                .WithParameters(customer, addrId)
                .Read<int>(@"
                    insert into example.customers (name, address_id) values ($1, $2) 
                    on conflict (name, address_id) do update set name = excluded.name, address_id = excluded.address_id 
                    returning customer_id")
                .Single();
        }

        connection
            .WithParameters(customerId, addrId)
            .Execute("insert into example.customer_addresses (customer_id, address_id) values ($1, $2)");
    }
}

connection.Execute("end");

Line($"Please wait while vacuuming database...");
connection.Execute("vacuum full");

Line($"Inserted {connection.Read<long>("select count(*) from example.customers").Single()} customers with {connection.Read<long>("select count(*) from example.addresses").Single()} addresses");
Line("Done!", ConsoleColor.Green);

static void Line(string message, ConsoleColor color = ConsoleColor.Yellow)
{
    WriteLine();
    ForegroundColor = color;
    WriteLine(message);
    ResetColor();
    WriteLine();
}
