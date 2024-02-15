namespace PageBenchmarks;
public class City
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
}

public class Address
{
    public int Id { get; set; }
    public string Street { get; set; } = default!;
    public City City { get; set; } = default!;
}

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
    public Address Address { get; set; } = default!;
    public long AddressCount { get; set; }
}

public class DataPage
{
    public long Count { get; set; }
    public List<Customer> Customers { get; set; } = default!;
}
