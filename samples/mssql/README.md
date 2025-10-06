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

Use `sqlcmd` (bundled with the container) to verify the sample stored procedures (flag `-C` trusts the self‑signed dev certificate):

```
docker exec -it spocr-sample-sql /opt/mssql-tools/bin/sqlcmd \
  -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d SpocRSample \
  -Q "EXEC samples.UserList"
```

The sample also includes JSON-producing procedures:

```
docker exec -it spocr-sample-sql /opt/mssql-tools/bin/sqlcmd \
  -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d SpocRSample \
  -Q "EXEC samples.OrderListAsJson"

docker exec -it spocr-sample-sql /opt/mssql-tools/bin/sqlcmd \
  -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d SpocRSample \
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
docker exec -it spocr-sample-sql /opt/mssql-tools/bin/sqlcmd   -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d SpocRSample   -Q "EXEC samples.UserDetailsWithOrders @UserId = 1"

docker exec -it spocr-sample-sql /opt/mssql-tools/bin/sqlcmd   -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d SpocRSample   -Q "EXEC samples.UserOrderHierarchyJson"

# Update the bio for user 1 using the scalar custom type
 docker exec -it spocr-sample-sql /opt/mssql-tools/bin/sqlcmd   -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d SpocRSample   -Q "EXEC samples.UserBioUpdate @UserId = 1, @Bio = N'Updated via sample'"

# Demonstrate table-valued parameter usage
 docker exec -it spocr-sample-sql /opt/mssql-tools/bin/sqlcmd   -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d SpocRSample   -Q "DECLARE @contacts samples.UserContactTableType; INSERT INTO @contacts (UserId, Email, DisplayName) VALUES (1, N'alice@example.com', N'Alice Example Updated'); EXEC samples.UserContactSync @Contacts = @contacts;"
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

## Full-Text Search (FTS)

Diese Standard-Variante basiert nun auf einem eigenen Ubuntu 20.04 (focal) Aufbau und installiert `mssql-server` sowie das Full‑Text Paket `mssql-server-fts` deterministisch während des Builds. Schlägt die Installation fehl, bricht der Build ab (Fail-Fast), sodass garantiert nur lauffähige Images mit aktiviertem FTS entstehen.

Das Skript `08-fulltext.sql` richtet beim Erststart einen Full‑Text Katalog (`SampleCatalog`) und den Index auf `samples.Users(DisplayName)` ein. Weitere optionale Indizes kannst du nach Bedarf hinzufügen (siehe unten).

Hinweis zum Wechsel von der früheren „best effort“ Variante: Falls du zuvor schon ein Volume ohne FTS erzeugt hattest, lösche es ( `docker compose down -v` ), damit das Initialisierungsskript erneut ausgeführt wird.

### Rebuild nach Änderungen

Wenn Du das Dockerfile (z.B. für FTS) geändert hast:

```
docker compose build --no-cache
docker compose up -d
```

### Überprüfen, ob Full-Text installiert & aktiv ist

```
docker exec -it spocr-sample-sql /opt/mssql-tools/bin/sqlcmd \
  -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d SpocRSample \
  -Q "SELECT SERVERPROPERTY('IsFullTextInstalled') AS ServerProp, FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') AS ServiceProp;"
```

Interpretation:

- ServerProp = 1 und ServiceProp = 1: FTS ist installiert und aktiv – `08-fulltext.sql` hat Katalog + Index erstellt.
- Einer oder beide Werte = 0: FTS-Paket steht für das verwendete Tag nicht zur Verfügung (oder Installation war nicht möglich); das Skript hat sauber übersprungen.

Da FTS jetzt erzwungen installiert wird, sollte folgende Prüfung immer beide Werte = 1 liefern. Falls nicht, ist beim Build etwas fehlgeschlagen und das Image sollte neu gebaut werden.

Zur Diagnose kannst du außerdem prüfen:

```
docker exec -it spocr-sample-sql cat /etc/os-release
docker logs spocr-sample-sql | findstr /i fulltext
```

### Kataloge & Indizes anzeigen

```
docker exec -it spocr-sample-sql /opt/mssql-tools/bin/sqlcmd \
  -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d SpocRSample \
  -Q "SELECT name FROM sys.fulltext_catalogs; SELECT object_name(object_id) AS TableName FROM sys.fulltext_indexes;"
```

### Beispiel Full-Text Query

Sucht nach Benutzern, deren `DisplayName` entweder "Alice" oder "Builder" enthält:

```
docker exec -it spocr-sample-sql /opt/mssql-tools/bin/sqlcmd \
  -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d SpocRSample \
  -Q "SELECT UserId, DisplayName, Bio FROM samples.Users WHERE CONTAINS(DisplayName, '""Alice"" OR ""Builder""');"
```

### Eigenen weiteren Full-Text Index hinzufügen

Beispiel für eine zusätzliche Tabelle / Spalte – hier `samples.Orders.Notes`:

1. Sicheren, deterministischen eindeutigen Schlüsselindex prüfen / anlegen (falls PK einen generierten Namen hat):

```
-- Beispiel (nur falls noch kein eindeutiger Index auf OrderId existiert):
CREATE UNIQUE INDEX UX_Orders_OrderId ON samples.Orders(OrderId);
```

2. Full‑Text Index erstellen:

```
CREATE FULLTEXT INDEX ON samples.Orders(Notes LANGUAGE 1031)
KEY INDEX UX_Orders_OrderId ON SampleCatalog WITH CHANGE_TRACKING AUTO;
```

> Hinweis: Der angegebene KEY INDEX muss exakt existieren; bei Verwendung des automatisch generierten PK-Namens vorab via `sp_helpindex samples.Orders` prüfen.

### Fehlerdiagnose (falls FTS wider Erwarten fehlt)

1. Logs prüfen: `docker compose logs sql-sample | findstr /i fts`
2. Image neu bauen ohne Cache: `docker compose build --no-cache`
3. Volume entfernen und erneut starten: `docker compose down -v && docker compose up -d`
4. Prüfen, ob externe Paketquelle erreichbar war (Netzwerk / Proxy).
