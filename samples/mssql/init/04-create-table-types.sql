USE SpocRSample;
GO

IF TYPE_ID(N'samples.UserContactTableType') IS NULL
BEGIN
    EXEC(N'CREATE TYPE samples.UserContactTableType AS TABLE (
        UserId INT NOT NULL,
        Email samples.EmailAddressType NOT NULL,
        DisplayName samples.DisplayNameType NOT NULL,
        PRIMARY KEY (UserId)
    );');
END
GO

IF TYPE_ID(N'samples.OrderImportTableType') IS NULL
BEGIN
    EXEC(N'CREATE TYPE samples.OrderImportTableType AS TABLE (
        UserId INT NOT NULL,
        TotalAmount samples.MoneyAmountType NOT NULL,
        PlacedAt samples.UtcDateTimeType NOT NULL
    );');
END
GO

IF TYPE_ID(N'samples.UserIdListTableType') IS NULL
BEGIN
    EXEC(N'CREATE TYPE samples.UserIdListTableType AS TABLE (
        UserId INT NOT NULL PRIMARY KEY
    );');
END
GO
