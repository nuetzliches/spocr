FROM ubuntu:focal

# Explicit SQL Server 2022 + Full-Text Search install on Ubuntu 20.04 (focal)
# This Dockerfile became the default variant to guarantee FTS availability.

ENV DEBIAN_FRONTEND=noninteractive \
    ACCEPT_EULA=Y \
    MSSQL_PID=Developer \
    PATH="${PATH}:/opt/mssql-tools/bin:/opt/mssql-tools18/bin"

RUN set -eux; \
    apt-get update; \
    apt-get install -y --no-install-recommends curl ca-certificates apt-transport-https gnupg software-properties-common locales; \
    locale-gen en_US.UTF-8; \
    update-locale LANG=en_US.UTF-8; \
    # Import Microsoft GPG key & repository config
    curl -fsSL https://packages.microsoft.com/keys/microsoft.asc | apt-key add -; \
    curl -fsSL https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -o /tmp/packages-microsoft-prod.deb; \
    dpkg -i /tmp/packages-microsoft-prod.deb; \
    rm /tmp/packages-microsoft-prod.deb; \
    # Ensure SQL Server 2022 repo list present (defensive)
    curl -fsSL https://packages.microsoft.com/config/ubuntu/20.04/mssql-server-2022.list | tee /etc/apt/sources.list.d/mssql-server-2022.list > /dev/null; \
    apt-get update; \
    # Engine
    apt-get install -y mssql-server; \
    # Full-Text (fail if missing)
    apt-get install -y mssql-server-fts; \
    # Tools (18) + compatibility symlink
    ACCEPT_EULA=Y apt-get install -y mssql-tools18 unixodbc-dev; \
    ln -s /opt/mssql-tools18 /opt/mssql-tools; \
    # Clean
    apt-get clean; \
    rm -rf /var/lib/apt/lists/*

# Create mssql user and directories
RUN useradd -r -u 10001 -g root mssql; \
    mkdir -p /var/opt/mssql /init /var/opt/mssql/log; \
    chown -R mssql:root /var/opt/mssql

COPY --chown=mssql:root init /init
COPY --chown=mssql:root scripts/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

EXPOSE 1433
USER mssql
ENV MSSQL_ENABLE_HADR=0
ENTRYPOINT ["/entrypoint.sh"]
CMD ["bash", "-c", "tail -f /var/opt/mssql/log/startup.log"]
