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