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
  -Q "EXEC samples.OrderListAsJson"

docker exec -it spocr-sample-sql /opt/mssql-tools/bin/sqlcmd \
  -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d SpocRSample \
  -Q "EXEC samples.OrderListByUserAsJson @UserId = 1"
```

### Advanced Test Procedures

The sample database also includes procedures that exercise more complex result shapes:

- `samples.UserDetailsWithOrders @UserId = 1` returns two result sets: the first contains the user profile, the second lists the related orders ordered by `PlacedAt`.
- `samples.UserOrderHierarchyJson` returns nested JSON where each user includes an `Orders` property with the JSON array of orders.
- `samples.UserBioUpdate @UserId, @Bio` accepts the custom scalar type `samples.UserBioType` and echoes the updated user record.
- `samples.UserContactSync @Contacts` accepts the table type `samples.UserContactTableType` and reports how many contacts were updated versus missing.

Examples:

```
docker exec -it spocr-sample-sql /opt/mssql-tools/bin/sqlcmd   -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d SpocRSample   -Q "EXEC samples.UserDetailsWithOrders @UserId = 1"

docker exec -it spocr-sample-sql /opt/mssql-tools/bin/sqlcmd   -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d SpocRSample   -Q "EXEC samples.UserOrderHierarchyJson"

# Update the bio for user 1 using the scalar custom type
 docker exec -it spocr-sample-sql /opt/mssql-tools/bin/sqlcmd   -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d SpocRSample   -Q "EXEC samples.UserBioUpdate @UserId = 1, @Bio = N'Updated via sample'"

# Demonstrate table-valued parameter usage
 docker exec -it spocr-sample-sql /opt/mssql-tools/bin/sqlcmd   -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d SpocRSample   -Q "DECLARE @contacts samples.UserContactTableType; INSERT INTO @contacts (UserId, Email, DisplayName) VALUES (1, N'alice@example.com', N'Alice Example Updated'); EXEC samples.UserContactSync @Contacts = @contacts;"
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
