USE SpocRSample;
GO

INSERT INTO samples.Users (Email, DisplayName)
VALUES
    (N'alice@example.com', N'Alice Example'),
    (N'bob@example.com', N'Bob Builder'),
    (N'charlie@example.com', N'Charlie Coder');
GO

INSERT INTO samples.Orders (UserId, TotalAmount, PlacedAt)
SELECT UserId, TotalAmount, PlacedAt
FROM (VALUES
    (1, 59.99, DATEADD(day, -5, SYSUTCDATETIME())),
    (1, 19.49, DATEADD(day, -2, SYSUTCDATETIME())),
    (2, 249.00, DATEADD(day, -1, SYSUTCDATETIME())),
    (3, 12.75, DATEADD(hour, -6, SYSUTCDATETIME()))
) AS Seed(UserId, TotalAmount, PlacedAt);
GO
