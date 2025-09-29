# SpocR Sample SQL Server Database

This sample provides a lightweight SQL Server instance with sample data for experimenting with SpocR code generation.

## Prerequisites

- Docker Desktop with the new `docker compose` CLI
- Copy `.env.example` to `.env` and provide a strong `MSSQL_SA_PASSWORD`
- The sample image is built locally via the provided `Dockerfile`, which installs `mssql-tools18` for `sqlcmd` support. Build happens automatically when running `docker compose up --build -d`.

```
cd samples/mssql
cp .env.example .env  # Windows: copy .env.example .env
```

> **Security**: never commit the `.env` file. Choose a password that satisfies SQL Server complexity rules (8+ chars, upper, lower, digit, symbol).

> **Note**: On first startup the container installs `mssql-tools18` inside the image so `sqlcmd` is available; this requires internet access.

## Start the Database

```
docker compose up --build -d
```

The container exposes SQL Server on `localhost,1433`.

Connection string example:

```
Server=localhost,1433;Database=SpocRSample;User ID=sa;Password=YourSecurePassword;Encrypt=True;TrustServerCertificate=True;
```

## Verify Sample Data

Use `sqlcmd` (bundled with the container) to verify the sample stored procedures:

```
docker exec -it spocr-sample-sql /opt/mssql-tools/bin/sqlcmd \
  -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d SpocRSample \
  -Q "EXEC samples.UserList"
```

The sample also includes JSON-producing procedures:

```
docker exec -it spocr-sample-sql /opt/mssql-tools/bin/sqlcmd \
  -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d SpocRSample \
  -Q "EXEC samples.OrderList1"

docker exec -it spocr-sample-sql /opt/mssql-tools/bin/sqlcmd \
  -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d SpocRSample \
  -Q "EXEC samples.OrderList2 @UserId = 1"
```

## Stopping & Cleanup

```
docker compose down
```

Add `-v` to remove the data volume (`./data`).

## Directory Layout

```
init/
  01-create-database.sql
  02-create-schema.sql
  03-create-scalar-types.sql
  04-create-table-types.sql
  05-create-tables.sql
  06-seed-data.sql
  07-create-procedures.sql
scripts/
  entrypoint.sh
```

The entrypoint script starts SQL Server, waits for readiness, and executes all scripts in `init/` in lexical order.
