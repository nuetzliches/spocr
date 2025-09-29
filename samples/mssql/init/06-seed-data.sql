USE SpocRSample;
GO

INSERT INTO samples.Users (Email, DisplayName, Bio)
VALUES
    (N'alice@example.com', N'Alice Example', N'Team lead and sample account owner'),
    (N'bob@example.com', N'Bob Builder', NULL),
    (N'charlie@example.com', N'Charlie Coder', N'Enjoys building JSON pipelines');
GO

INSERT INTO samples.Orders (UserId, TotalAmount, PlacedAt, Notes)
SELECT UserId, TotalAmount, PlacedAt, Notes
FROM (VALUES
    (1, 59.99, DATEADD(day, -5, SYSUTCDATETIME()), N'Includes express shipping'),
    (1, 19.49, DATEADD(day, -2, SYSUTCDATETIME()), NULL),
    (2, 249.00, DATEADD(day, -1, SYSUTCDATETIME()), N'Bundle discount applied'),
    (3, 12.75, DATEADD(hour, -6, SYSUTCDATETIME()), NULL)
) AS Seed(UserId, TotalAmount, PlacedAt, Notes);
GO
