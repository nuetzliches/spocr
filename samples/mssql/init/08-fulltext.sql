USE SpocRSample;
GO

/*
 Refactored: Single batch logic guarded by variables to avoid executing FTS statements
 when Full-Text components are not installed. The previous version used multiple GO
 batches; RETURN only exits the current batch so subsequent batches still ran.
*/

DECLARE @IsInstalled INT = CONVERT(INT, SERVERPROPERTY('IsFullTextInstalled'));
DECLARE @IsService INT = CONVERT(INT, FULLTEXTSERVICEPROPERTY('IsFullTextInstalled'));
DECLARE @CanLoad bit = 0;

/* Additional runtime heuristic: if system stored proc for fulltext exists */
IF OBJECT_ID('sys.sp_fulltext_service') IS NOT NULL SET @CanLoad = 1;

PRINT 'FTS pre-check: IsInstalled=' + COALESCE(CONVERT(varchar(10),@IsInstalled),'NULL') + ', Service=' + COALESCE(CONVERT(varchar(10),@IsService),'NULL') + ', CanLoad=' + CONVERT(varchar(1),@CanLoad);

IF (@IsInstalled <> 1 OR @IsService <> 1 OR @CanLoad = 0)
BEGIN
    PRINT 'Full-Text components not fully available. Skipping FTS catalog/index creation.';
END
ELSE
BEGIN
    BEGIN TRY
    -- Create Full-Text Catalog if not exists
    IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = N'SampleCatalog')
    BEGIN
        PRINT 'Creating Full-Text Catalog SampleCatalog';
        CREATE FULLTEXT CATALOG SampleCatalog AS DEFAULT;
    END;

    -- Ensure deterministic unique index for key
        IF NOT EXISTS (
                SELECT 1 FROM sys.indexes i
                    JOIN sys.objects o ON i.object_id = o.object_id
                    JOIN sys.schemas s ON o.schema_id = s.schema_id
        WHERE s.name = N'samples' AND o.name = N'Users' AND i.name = N'UX_Users_UserId'
    )
    BEGIN
        PRINT 'Creating supporting unique index UX_Users_UserId';
        CREATE UNIQUE INDEX UX_Users_UserId ON samples.Users(UserId);
    END;

    -- Create FTS index if missing (only if catalog creation didn't error)
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
            DisplayName LANGUAGE 1031,
            Bio LANGUAGE 1031
        ) KEY INDEX UX_Users_UserId ON SampleCatalog WITH CHANGE_TRACKING AUTO;
    END;
        PRINT 'Full-Text setup completed (catalog & index ensured).';
    END TRY
    BEGIN CATCH
        PRINT 'FTS setup encountered error ' + CONVERT(varchar(10), ERROR_NUMBER()) + ' at line ' + CONVERT(varchar(10), ERROR_LINE()) + ': ' + ERROR_MESSAGE();
        PRINT 'Continuing without failing initialization.';
    END CATCH
END;

SELECT SERVERPROPERTY('IsFullTextInstalled') AS IsFullTextInstalled; -- 1 expected if installed
