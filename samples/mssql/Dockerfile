FROM mcr.microsoft.com/mssql/server:2022-latest

USER root
ENV ACCEPT_EULA=Y
RUN apt-get update \
    && apt-get install -y mssql-tools18 unixodbc-dev \
    && ln -s /opt/mssql-tools18 /opt/mssql-tools \
    && rm -rf /var/lib/apt/lists/*
ENV PATH="${PATH}:/opt/mssql-tools/bin:/opt/mssql-tools18/bin"
USER mssql
