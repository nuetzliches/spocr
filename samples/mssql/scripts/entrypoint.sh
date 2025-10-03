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

wait_for_database() {
  local db="$1"
  for i in $(seq 1 60); do
    local status
    set +e
    status=$(/opt/mssql-tools/bin/sqlcmd -S localhost -d master -U sa -P "${MSSQL_SA_PASSWORD}" -C -h-1 -W <<SQL 2>/dev/null
SET NOCOUNT ON;
DECLARE @state NVARCHAR(60) = CASE
  WHEN DB_ID(N'${db}') IS NULL THEN N'MISSING'
  ELSE CONVERT(NVARCHAR(60), DATABASEPROPERTYEX(N'${db}', 'STATUS'))
END;
SELECT @state;
SQL
)
    local query_exit=$?
    set -e

    if [[ ${query_exit} -ne 0 ]]; then
      status=""
    fi

    # Normalize output (remove control chars & spaces); keep a copy for debugging
    local raw_status="${status}"
    status=$(echo "${status}" | tr -d '\r\n' | xargs 2>/dev/null || true)

    if [[ "${status}" == "ONLINE" ]]; then
      if /opt/mssql-tools/bin/sqlcmd -S localhost -d "${db}" -U sa -P "${MSSQL_SA_PASSWORD}" -C -Q "SELECT 1" >/dev/null 2>&1; then
        if (( i > 1 )); then
          echo "Database ${db} is ONLINE (became accessible after ${i}s)."
        else
          echo "Database ${db} is already ONLINE."
        fi
        return 0
      fi
    fi

    if (( i == 1 )); then
      # Simplified raw output display (strip newlines only)
      local raw_display=$(echo "${raw_status}" | tr -d '\r\n')
      echo "Waiting for database ${db} to become accessible (status: ${status:-unknown}; raw=\"${raw_display}\")..."
    elif (( i % 5 == 0 )); then
      echo "Still waiting for database ${db} (status: ${status:-unknown})"
    fi

    # Fallback: If status empty but direct connect to db works, proceed
    if [[ -z "${status}" || "${status}" == "MISSING" ]]; then
      if /opt/mssql-tools/bin/sqlcmd -S localhost -d "${db}" -U sa -P "${MSSQL_SA_PASSWORD}" -C -Q "SELECT 1" >/dev/null 2>&1; then
        echo "Database ${db} appears accessible despite status='${status:-empty}'. Continuing."; return 0; fi
    fi
    sleep 1
  done

  echo "Database ${db} did not become accessible in time." >&2
  return 1
}

shopt -s nullglob
for script in /init/*.sql; do
  base=$(basename "${script}")
  echo "Executing ${script}"
  /opt/mssql-tools/bin/sqlcmd -S localhost -d master -U sa -P "${MSSQL_SA_PASSWORD}" -C -i "${script}"

  if [[ "${base}" == "01-create-database.sql" ]]; then
    wait_for_database "SpocRSample"
  fi

done
shopt -u nullglob

wait "$SQL_PID"
