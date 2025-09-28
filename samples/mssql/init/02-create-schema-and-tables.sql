USE SpocRSample;
GO

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'samples')
BEGIN
    EXEC('CREATE SCHEMA samples');
END
GO

IF OBJECT_ID(N'samples.Users', N'U') IS NULL
BEGIN
    CREATE TABLE samples.Users
    (
        UserId INT IDENTITY(1,1) PRIMARY KEY,
        Email NVARCHAR(256) NOT NULL,
        DisplayName NVARCHAR(128) NOT NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

IF OBJECT_ID(N'samples.Orders', N'U') IS NULL
BEGIN
    CREATE TABLE samples.Orders
    (
        OrderId INT IDENTITY(1,1) PRIMARY KEY,
        UserId INT NOT NULL,
        TotalAmount DECIMAL(10,2) NOT NULL,
        PlacedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_Orders_Users FOREIGN KEY (UserId) REFERENCES samples.Users(UserId)
    );
END
GO
