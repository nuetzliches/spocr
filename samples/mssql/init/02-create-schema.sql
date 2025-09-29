USE SpocRSample;
GO

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'samples')
BEGIN
    EXEC(N'CREATE SCHEMA samples');
END
GO
