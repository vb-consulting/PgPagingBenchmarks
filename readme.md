# PostgreSQL Paging Benchmarks

[Blog Post PostgreSQL Paging Benchmarks](https://vb-consulting.github.io/blog/postgresql-paging/)

This is a performance benchmark project that tests different data paging approaches in PostgreSQL. You can find the source code in the [PgPagingBenchmarks](https://github.com/vb-consulting/PgPagingBenchmarks) repository.

## Setup

PostgreSQL 16 instance on my laptop:

```console
select version();
PostgreSQL 16.0 (Ubuntu 16.0-1.pgdg20.04+1) on x86_64-pc-linux-gnu, compiled by gcc (Ubuntu 9.4.0-1ubuntu1~20.04.2) 9.4.0, 64-bit
```

Initial schema ([source](https://github.com/vb-consulting/PgPagingBenchmarks/blob/master/Db/script.sql)):

```sql
begin;

drop schema if exists example cascade;
create schema example;

create table example.cities (
    city_id int generated always as identity primary key,
    name text not null unique
);

create table example.addresses (
    address_id int generated always as identity primary key,
    street text not null,
    city_id int not null references example.cities deferrable,
    unique (street, city_id)
);

create table example.customers (
    customer_id int generated always as identity primary key,
    name text not null,
    address_id int not null references example.addresses deferrable,
    unique (name, address_id)
);

create table example.customer_addresses (
    customer_id int not null references example.customers deferrable,
    address_id int not null references example.addresses deferrable,
    primary key (customer_id, address_id)
);

alter table example.customers 
add constraint fk_customer_addresses
foreign key (customer_id, address_id) 
references example.customer_addresses deferrable;

end;
```

Tables were seeded with initial data using the Faker Bogus library ([source script](https://github.com/vb-consulting/PgPagingBenchmarks/blob/master/Db/Program.cs)). 


## Requirements

The query that was tested is this:

```sql
select 
    customers.customer_id,
    customers.name,
    customers.address_id,
    street,
    cities.city_id,
    cities.name
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
```

- The query needs to return customer data along with the default address.
- The query also needs to return a total count of customer addresses.
- The query needs to return the last page with a length of 10 rows. In this case, this is the page 625.
- Query needs to be filtered by customer name pattern. In this case this `%john%` pattern.
- Total rows unpaged count is also required.
- Pages data needs to be sorted by the customer name.

And finally, results need to be serialized into the following structure:

```csharp
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
```

As we can see, keyset paging is not possible. However, there are a few other possibilities we can test and explore.

## Test methods

### Method 1

SQL scripts in two steps:

- The first step: get the total count from the filtered table.
- The second step: use limit and offset on a query to get the page.
- Map results by position from the reader fields.
- [Source Code](https://github.com/vb-consulting/PgPagingBenchmarks/blob/master/PageBenchmarks/Method1.cs)

```sql
select count(*)  from example.customers where name ilike $1
```

```sql
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
```

### Method 2

Single SQL query:

- The table is filtered in materialized CTE.
- Joins CTE with the main query.
- Use limit and offset on a query to get the page.
- The count is in additional subquery: `(select count(*) from cte) as count`
- Map results by position from the reader fields.
- [Source Code](https://github.com/vb-consulting/PgPagingBenchmarks/blob/master/PageBenchmarks/Method2.cs)

```sql
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
```

### Method 3

Single SQL query:

- The table is filtered in materialized CTE that includes the row number.
- Joins CTE with the main query.
- Filter by the row number to get the page.
- Order by the customer is in the materialized CTE.
- The count is in the additional subquery that finds the max row number: `(select max(row) from cte) as count`
- [Source Code](https://github.com/vb-consulting/PgPagingBenchmarks/blob/master/PageBenchmarks/Method3.cs)

```sql
with cte as materialized (
    select row_number() over() as row, customer_id 
    from example.customers 
    where name ilike $1
    order by customers.name
)
select 
    customers.customer_id,
    customers.name,
    customers.address_id,
    street,
    cities.city_id,
    cities.name,
    count(*) as address_count,
    (select max(row) from cte) as count
from 
    cte
    join example.customers using (customer_id)
    join example.addresses using (address_id)
    join example.cities using (city_id) 
    join example.customer_addresses using (customer_id)
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
```

### Method 4

SQL script that creates the TEMP table within a transaction:

- Create transaction.
- Create the TEMP table from filtered data.
- Return count from the TEMP table.
- Join the TEMP table with the main query and use the offset and limit to page the data.
- [Source Code](https://github.com/vb-consulting/PgPagingBenchmarks/blob/master/PageBenchmarks/Method4.cs)

```sql
begin
```

```sql
create temp table _temp_customers on commit drop as
select customer_id from example.customers where name ilike $1
```

```sql
select count(*) from _temp_customers
```

```sql
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
```

```sql
end
```

### Method 5

SQL script that creates the TEMP table with row number within a transaction:

- Create transaction.
- Create the TEMP table with the row number from filtered data and order by customer.
- Return the max row from the TEMP table.
- Join the TEMP table with the main query and use the row number to page the data.
- Order by row number.
- [Source Code](https://github.com/vb-consulting/PgPagingBenchmarks/blob/master/PageBenchmarks/Method5.cs)

```sql
begin
```

```sql
create temp table _temp_customers on commit drop as
select row_number() over() as row, customer_id 
from example.customers 
where name ilike $1
order by customers.name
```

```sql
select max(row) from _temp_customers
```

```sql
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
```

```sql
end
```

### Method 6

SQL script that creates the indexed TEMP table with row number within a transaction:

- Create transaction.
- Create the TEMP table with the row number from filtered data and order by customer.
- Create the BTREE index on the TEMP table.
- Return the max row from the TEMP table.
- Join the TEMP table with the main query and use the row number to page the data.
- Order by row number.
- [Source Code](https://github.com/vb-consulting/PgPagingBenchmarks/blob/master/PageBenchmarks/Method6.cs)

```sql
begin
```

```sql
create temp table _temp_customers on commit drop as
select row_number() over() as row, customer_id 
from example.customers 
where name ilike $1
order by customers.name
```

```sql
create index on _temp_customers using btree (row)
```

```sql
select max(row) from _temp_customers
```

```sql
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
```

```sql
end
```

### Method 7

PLPGSQL function that returns JSON:

- Create the TEMP table from filtered data.
- Get the count from the inserted diagnostics.
- Join the TEMP table with the main query and use thelimt and offset to page the data.
- Build a JSON response and deserialize it on the client.
- [Source Code](https://github.com/vb-consulting/PgPagingBenchmarks/blob/master/PageBenchmarks/Method7.cs)

```sql
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
```

[source](https://github.com/vb-consulting/PgPagingBenchmarks/blob/master/PageBenchmarks/Method7.cs)

### Method 8

PLPGSQL function that returns JSON:

- Create the TEMP table with the row number from filtered data and order by customer.
- Get the count from the inserted diagnostics.
- Join the TEMP table with the main query and use the row number to page the data.
- Build a JSON response and deserialize it on the client.
- [Source Code](https://github.com/vb-consulting/PgPagingBenchmarks/blob/master/PageBenchmarks/Method8.cs)

```sql
create or replace function example.method8(
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
    select row_number() over() as row, customer_id 
    from example.customers 
    where name ilike _search
    order by customers.name;
    
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

            ) sub
        )

    );
end
$$;

```

### Method 9

PLPGSQL function that returns JSON:

- Create the indexed TEMP table with the row number from filtered data and order by customer.
- Get the count from the inserted diagnostics.
- Create the BTREE index on the TEMP table.
- Join the TEMP table with the main query and use the row number to page the data.
- Build a JSON response and deserialize it on the client.
- [Source Code](https://github.com/vb-consulting/PgPagingBenchmarks/blob/master/PageBenchmarks/Method9.cs)


```sql
create or replace function example.method9(
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
    select row_number() over() as row, customer_id 
    from example.customers 
    where name ilike _search
    order by customers.name;
    
    get diagnostics _count = row_count;

    create index on _temp_customers using btree (row);
    
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

            ) sub
        )

    );
end
$$;
```

### Method 10

PLPGSQL function that returns TABLE:

- Create the indexed TEMP table with the row number from filtered data and order by customer.
- Get the count from the inserted diagnostics.
- Create the BTREE index on the TEMP table.
- Join the TEMP table with the main query and use the row number to page the data.
- Return the table and map by position on the client.
- [Source Code](https://github.com/vb-consulting/PgPagingBenchmarks/blob/master/PageBenchmarks/Method10.cs)

```sql
create or replace function example.method10(
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
language plpgsql 
as $$
declare
    _count bigint;
begin
    create temp table _temp_customers on commit drop as
    select row_number() over() as row, customer_id 
    from example.customers 
    where name ilike _search
    order by customers.name;
    
    get diagnostics _count = row_count;

    create index on _temp_customers using btree (row);
    
    return query
    select 
        customers.customer_id,
        customers.name,
        customers.address_id,
        street,
        cities.city_id,
        cities.name,
        count(*) as address_count,
        _count
    from 
        _temp_customers
        join example.customers using (customer_id)
        join example.addresses using (address_id)
        join example.cities using (city_id)
        join example.customer_addresses using (customer_id)
    where row > $2 and row <= $2 + $3
    group by
        row,
        customers.customer_id,
        customers.name,
        customers.address_id,
        street,
        cities.city_id,
        cities.name
    order by row;
end
$$;
```

### Method 11

SQL function that returns TABLE:

- Use un-materialized CTE without ROW numbers.
- Same as [method 2](#method-2), only in SQL function.
- Notes: labeling function as STABLE or CTE as MATERIALIZED degrades performances.
- [Source Code](https://github.com/vb-consulting/PgPagingBenchmarks/blob/master/PageBenchmarks/Method11.cs)

```sql
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
```

### Method 12

SQL function that returns TABLE:

- Use un-materialized CTE with ROW numbers.
- Same as [method 3](#method-3), only in SQL function.
- Notes: labeling function as STABLE or CTE as MATERIALIZED degrades performances.
- [Source Code](https://github.com/vb-consulting/PgPagingBenchmarks/blob/master/PageBenchmarks/Method12.cs)

```sql
create or replace function example.method12(
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
    select row_number() over() as row, customer_id 
    from example.customers 
    where name ilike _search
    order by customers.name
)
select 
    customers.customer_id,
    customers.name,
    customers.address_id,
    street,
    cities.city_id,
    cities.name,
    count(*) as address_count,
    (select max(row) from cte) as count
from 
    cte
    join example.customers using (customer_id)
    join example.addresses using (address_id)
    join example.cities using (city_id) 
    join example.customer_addresses using (customer_id)
where row > _skip and row <= _skip + _take
group by
    row,
    customers.customer_id,
    customers.name,
    customers.address_id,
    street,
    cities.city_id,
    cities.name
order by row
$$;
```

### Method 13

SQL query:

- Use un-materialized CTE without ROW numbers.
- Same as [method 2](#method-3), only without materialization.
- [Source Code](https://github.com/vb-consulting/PgPagingBenchmarks/blob/master/PageBenchmarks/Method13.cs)

```sql
with cte as (
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
```

### Method 14

SQL query:

- Use un-materialized CTE with ROW numbers.
- Same as [method 3](#method-3), only without materialization.
- [Source Code](https://github.com/vb-consulting/PgPagingBenchmarks/blob/master/PageBenchmarks/Method14.cs)

```sql
with cte as (
    select row_number() over() as row, customer_id 
    from example.customers 
    where name ilike $1
    order by customers.name
)
select 
    customers.customer_id,
    customers.name,
    customers.address_id,
    street,
    cities.city_id,
    cities.name,
    count(*) as address_count,
    (select max(row) from cte) as count
from 
    cte
    join example.customers using (customer_id)
    join example.addresses using (address_id)
    join example.cities using (city_id) 
    join example.customer_addresses using (customer_id)
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
```

## Results

### Round1

| Table  | Count    |
|-------- |--------:|
| `example.cities` | 432,166 |
| `example.addresses` | 3,996,491 |
| `example.customers` | 500,000 |
| `example.customer_addresses` | 3,996,491 |

| Method  | Mean    | Error    | StdDev   | Ratio |
|-------- |--------:|---------:|---------:|------:|
| [Method1](#method-1) | 2.239 s | 0.0204 s | 0.0190 s |  1.00 |
| [Method2](#method-2) | 2.267 s | 0.0168 s | 0.0157 s |  1.01 |
| [Method3](#method-3) | 2.133 s | 0.0139 s | 0.0130 s |  0.95 |
| [Method4](#method-4) | 2.273 s | 0.0120 s | 0.0113 s |  1.02 |
| [Method5](#method-5) | 2.154 s | 0.0235 s | 0.0220 s |  0.96 |
| [Method6](#method-6) | 2.137 s | 0.0157 s | 0.0147 s |  0.95 |
| [Method7](#method-7) | 2.316 s | 0.0144 s | 0.0135 s |  1.03 |
| [Method8](#method-8) | 2.136 s | 0.0178 s | 0.0167 s |  0.95 |
| ~~[Method9](#method-9)~~ | ~~2.147 s~~ | ~~0.0153 s~~ | ~~0.0143 s~~ |  ~~0.96~~ |

* Method 9 mistakenly was executing method 8, that result is not valid.

### Round2

| Table  | Count    |
|-------- |--------:|
| `example.cities` | 450,762 |
| `example.addresses` | 5,596,217 |
| `example.customers` | 700,000 |
| `example.customer_addresses` | 5,596,218 |

| Method   | Mean    | Error    | StdDev   | Ratio | RatioSD |
|--------- |--------:|---------:|---------:|------:|--------:|
| [Method1](#method-1)   | 2.317 s | 0.0157 s | 0.0147 s |  1.00 |    0.00 |
| [Method2](#method-2)   | 2.339 s | 0.0128 s | 0.0113 s |  1.01 |    0.01 |
| [Method3](#method-3)   | 2.153 s | 0.0159 s | 0.0149 s |  0.93 |    0.01 |
| [Method4](#method-4)   | 2.387 s | 0.0456 s | 0.0593 s |  1.03 |    0.03 |
| [Method5](#method-5)   | 2.163 s | 0.0156 s | 0.0146 s |  0.93 |    0.01 |
| [Method6](#method-6)   | 2.167 s | 0.0105 s | 0.0093 s |  0.94 |    0.01 |
| [Method7](#method-7)   | 2.402 s | 0.0177 s | 0.0165 s |  1.04 |    0.01 |
| [Method8](#method-8)   | 2.159 s | 0.0147 s | 0.0137 s |  0.93 |    0.01 |
| [Method9](#method-9)   | 2.167 s | 0.0168 s | 0.0157 s |  0.94 |    0.01 |
| [Method10](#method-10)  | 2.160 s | 0.0146 s | 0.0130 s |  0.93 |    0.01 |
| [Method11](#method-11)  | 2.231 s | 0.0178 s | 0.0167 s |  0.96 |    0.01 |
| [Method12](#method-12)  | 2.176 s | 0.0422 s | 0.0414 s |  0.94 |    0.02 |
| [Method13](#method-13)  | 2.338 s | 0.0195 s | 0.0173 s |  1.01 |    0.01 |
| [Method14](#method-14)  | 2.156 s | 0.0121 s | 0.0113 s |  0.93 |    0.01 |

## Conclusion

PostgreSQL 16 is incredibly optimized. I'm Gonna Need a Bigger Boat, I mean the dataset.

Edit, round2:

- With a slightly bigger set difference is slightly bigger, but still in the milliseconds range and not worth optimizing further.
- Surprisingly CTE methods are a bit slower when not materialized. Probably because they fit into memory, on bigger result sets this should not be the case. 
- Even more surprisingly, functions labeled as STABLE will experience a serious degradation in performance, which was unexpected.
- SQL function in [method 12](#method-12) was even returning a wrong page when labeled as STABLE and was using MATERIALIZED CTE.

>
> If anyone has a different method, let me know, I'd like to include that in tests too.
>