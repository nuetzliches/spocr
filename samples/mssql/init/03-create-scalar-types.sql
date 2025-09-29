USE SpocRSample;
GO

IF TYPE_ID(N'samples.EmailAddressType') IS NULL
BEGIN
    EXEC(N'CREATE TYPE samples.EmailAddressType FROM NVARCHAR(256) NOT NULL;');
END
GO

IF TYPE_ID(N'samples.DisplayNameType') IS NULL
BEGIN
    EXEC(N'CREATE TYPE samples.DisplayNameType FROM NVARCHAR(128) NOT NULL;');
END
GO

IF TYPE_ID(N'samples.MoneyAmountType') IS NULL
BEGIN
    EXEC(N'CREATE TYPE samples.MoneyAmountType FROM DECIMAL(18,2) NOT NULL;');
END
GO

IF TYPE_ID(N'samples.UtcDateTimeType') IS NULL
BEGIN
    EXEC(N'CREATE TYPE samples.UtcDateTimeType FROM DATETIME2(7) NOT NULL;');
END
GO

IF TYPE_ID(N'samples.UserBioType') IS NULL
BEGIN
    EXEC(N'CREATE TYPE samples.UserBioType FROM NVARCHAR(512) NULL;');
END
GO

IF TYPE_ID(N'samples.OrderNoteType') IS NULL
BEGIN
    EXEC(N'CREATE TYPE samples.OrderNoteType FROM NVARCHAR(1024) NULL;');
END
GO
