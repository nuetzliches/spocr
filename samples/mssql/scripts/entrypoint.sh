#!/bin/bash
set -e

export PATH="${PATH}:/opt/mssql-tools/bin:/opt/mssql-tools18/bin"

mkdir -p /var/opt/mssql/log

/opt/mssql/bin/sqlservr >/var/opt/mssql/log/startup.log 2>&1 &
SQL_PID=$!

for i in $(seq 1 60); do
  /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "${MSSQL_SA_PASSWORD}" -C -Q "SELECT 1" >/dev/null 2>&1 && break
  sleep 1
done

if ! kill -0 "$SQL_PID" >/dev/null 2>&1; then
  echo "SQL Server process is not running. Exiting."
  exit 1
fi

shopt -s nullglob
for script in /init/*.sql; do
  echo "Executing ${script}"
  /opt/mssql-tools/bin/sqlcmd -S localhost -d master -U sa -P "${MSSQL_SA_PASSWORD}" -C -i "${script}"
done
shopt -u nullglob

wait "$SQL_PID"