USE SpocRSample;
GO

-- Full-Text Search Hinweis:
-- Das offizielle Microsoft SQL Server 2022 Linux Image bringt Full-Text Komponenten bereits mit.
-- Es ist kein separates apt-Paket (z.B. mssql-server-fts) erforderlich. Dieses Skript richtet nur
-- Katalog und Index ein, falls noch nicht vorhanden.

-- Create Full-Text Catalog if not exists
IF NOT EXISTS (
    SELECT 1 FROM sys.fulltext_catalogs WHERE name = N'SampleCatalog'
)
BEGIN
    PRINT 'Creating Full-Text Catalog SampleCatalog';
    CREATE FULLTEXT CATALOG SampleCatalog AS DEFAULT;
END
GO

-- Ensure a deterministic unique index for FTS key (system generated PK names vary)
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes i
      JOIN sys.objects o ON i.object_id = o.object_id
      JOIN sys.schemas s ON o.schema_id = s.schema_id
    WHERE s.name = N'samples' AND o.name = N'Users' AND i.name = N'UX_Users_UserId'
)
BEGIN
    PRINT 'Creating supporting unique index UX_Users_UserId';
    CREATE UNIQUE INDEX UX_Users_UserId ON samples.Users(UserId);
END
GO

-- Create FTS index if missing
IF NOT EXISTS (
    SELECT 1
    FROM sys.fulltext_indexes fti
        JOIN sys.objects o ON fti.object_id = o.object_id
        JOIN sys.schemas s ON o.schema_id = s.schema_id
    WHERE s.name = N'samples' AND o.name = N'Users'
)
BEGIN
    PRINT 'Creating Full-Text Index on samples.Users';
    CREATE FULLTEXT INDEX ON samples.Users
    (
        DisplayName LANGUAGE 1031,  -- German
        Bio LANGUAGE 1031
    ) KEY INDEX UX_Users_UserId ON SampleCatalog WITH CHANGE_TRACKING AUTO;
END
GO

-- Simple demo query (will return rows containing phrase 'Builder' or 'Alice')
PRINT 'Full-Text Search demo queries:';
SELECT UserId, DisplayName, Bio
FROM samples.Users
WHERE CONTAINS(DisplayName, '"Alice" OR "Builder"');
GO

-- Show that the full-text components are installed
SELECT SERVERPROPERTY('IsFullTextInstalled') AS IsFullTextInstalled; -- 1 expected
GO
