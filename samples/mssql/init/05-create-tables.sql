USE SpocRSample;
GO

IF OBJECT_ID(N'samples.Users', N'U') IS NULL
BEGIN
    CREATE TABLE samples.Users
    (
        UserId INT IDENTITY(1,1) PRIMARY KEY,
        Email samples.EmailAddressType NOT NULL,
        DisplayName samples.DisplayNameType NOT NULL,
        CreatedAt samples.UtcDateTimeType NOT NULL DEFAULT SYSUTCDATETIME(),
        Bio samples.UserBioType NULL
    );
END
ELSE
BEGIN
    -- Ensure existing columns use the latest custom data types
    IF COL_LENGTH('samples.Users', 'Email') IS NOT NULL
    BEGIN
        EXEC sp_executesql N'ALTER TABLE samples.Users ALTER COLUMN Email samples.EmailAddressType NOT NULL';
    END;

    IF COL_LENGTH('samples.Users', 'DisplayName') IS NOT NULL
    BEGIN
        EXEC sp_executesql N'ALTER TABLE samples.Users ALTER COLUMN DisplayName samples.DisplayNameType NOT NULL';
    END;

    IF COL_LENGTH('samples.Users', 'CreatedAt') IS NOT NULL
    BEGIN
        EXEC sp_executesql N'ALTER TABLE samples.Users ALTER COLUMN CreatedAt samples.UtcDateTimeType NOT NULL';
    END;

    IF COL_LENGTH('samples.Users', 'Bio') IS NULL
    BEGIN
        EXEC sp_executesql N'ALTER TABLE samples.Users ADD Bio samples.UserBioType NULL';
    END;
END
GO

IF OBJECT_ID(N'samples.Orders', N'U') IS NULL
BEGIN
    CREATE TABLE samples.Orders
    (
        OrderId INT IDENTITY(1,1) PRIMARY KEY,
        UserId INT NOT NULL,
        TotalAmount samples.MoneyAmountType NOT NULL,
        PlacedAt samples.UtcDateTimeType NOT NULL DEFAULT SYSUTCDATETIME(),
        Notes samples.OrderNoteType NULL,
        CONSTRAINT FK_Orders_Users FOREIGN KEY (UserId) REFERENCES samples.Users(UserId)
    );
END
ELSE
BEGIN
    IF COL_LENGTH('samples.Orders', 'TotalAmount') IS NOT NULL
    BEGIN
        EXEC sp_executesql N'ALTER TABLE samples.Orders ALTER COLUMN TotalAmount samples.MoneyAmountType NOT NULL';
    END;

    IF COL_LENGTH('samples.Orders', 'PlacedAt') IS NOT NULL
    BEGIN
        EXEC sp_executesql N'ALTER TABLE samples.Orders ALTER COLUMN PlacedAt samples.UtcDateTimeType NOT NULL';
    END;

    IF COL_LENGTH('samples.Orders', 'Notes') IS NULL
    BEGIN
        EXEC sp_executesql N'ALTER TABLE samples.Orders ADD Notes samples.OrderNoteType NULL';
    END;
END
GO
