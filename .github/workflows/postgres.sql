create table users(
    id serial primary key,
    email text not null,
    date_of_birth timestamptz not null,
    created timestamptz not null default now()
);
