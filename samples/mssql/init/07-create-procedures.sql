USE SpocRSample;
GO

CREATE OR ALTER PROCEDURE samples.UserList
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        UserId      = CAST(UserId AS INT),
        Email       = CAST(Email AS NVARCHAR(256)),
        DisplayName = CAST(DisplayName AS NVARCHAR(128)),
        CreatedAt   = CAST(CreatedAt AS DATETIME2(7)),
        Bio         = CAST(Bio AS NVARCHAR(512))
    FROM samples.Users
    ORDER BY DisplayName;
END
GO

CREATE OR ALTER PROCEDURE samples.UserFind
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        UserId      = CAST(UserId AS INT),
        Email       = CAST(Email AS NVARCHAR(256)),
        DisplayName = CAST(DisplayName AS NVARCHAR(128)),
        CreatedAt   = CAST(CreatedAt AS DATETIME2(7)),
        Bio         = CAST(Bio AS NVARCHAR(512))
    FROM samples.Users
    WHERE UserId = @UserId;
END
GO

CREATE OR ALTER PROCEDURE samples.OrderListAsJson
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        UserId      = CAST(u.UserId AS INT),
        DisplayName = CAST(u.DisplayName AS NVARCHAR(128)),
        Email       = CAST(u.Email AS NVARCHAR(256)),
        OrderId     = CAST(o.OrderId AS INT),
        TotalAmount = CAST(o.TotalAmount AS DECIMAL(18, 2)),
        PlacedAt    = CAST(o.PlacedAt AS DATETIME2(7)),
        Notes       = CAST(o.Notes AS NVARCHAR(1024))
    FROM samples.Users AS u
        LEFT JOIN samples.Orders AS o ON o.UserId = u.UserId
    ORDER BY u.UserId, o.PlacedAt
    FOR JSON PATH, ROOT('OrderSummaries');

END
GO

CREATE OR ALTER PROCEDURE samples.OrderListByUserAsJson
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        UserId      = CAST(u.UserId AS INT),
        DisplayName = CAST(u.DisplayName AS NVARCHAR(128)),
        Email       = CAST(u.Email AS NVARCHAR(256)),
        OrderId     = CAST(o.OrderId AS INT),
        TotalAmount = CAST(o.TotalAmount AS DECIMAL(18, 2)),
        PlacedAt    = CAST(o.PlacedAt AS DATETIME2(7)),
        Notes       = CAST(o.Notes AS NVARCHAR(1024))
    FROM samples.Users AS u
        LEFT JOIN samples.Orders AS o ON o.UserId = u.UserId
    WHERE u.UserId = @UserId
    ORDER BY o.PlacedAt
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;

END
GO

CREATE OR ALTER PROCEDURE samples.UserDetailsWithOrders
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        UserId      = CAST(u.UserId AS INT),
        DisplayName = CAST(u.DisplayName AS NVARCHAR(128)),
        Email       = CAST(u.Email AS NVARCHAR(256)),
        CreatedAt   = CAST(u.CreatedAt AS DATETIME2(7)),
        Bio         = CAST(u.Bio AS NVARCHAR(512))
    FROM samples.Users AS u
    WHERE u.UserId = @UserId;

    SELECT
        OrderId     = CAST(o.OrderId AS INT),
        TotalAmount = CAST(o.TotalAmount AS DECIMAL(18, 2)),
        PlacedAt    = CAST(o.PlacedAt AS DATETIME2(7)),
        Notes       = CAST(o.Notes AS NVARCHAR(1024))
    FROM samples.Orders AS o
    WHERE o.UserId = @UserId
    ORDER BY o.PlacedAt;
END
GO

CREATE OR ALTER PROCEDURE samples.UserOrderHierarchyJson
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        UserId      = CAST(u.UserId AS INT),
        DisplayName = CAST(u.DisplayName AS NVARCHAR(128)),
        Email       = CAST(u.Email AS NVARCHAR(256)),
        Orders = (
            SELECT
                OrderId     = CAST(o.OrderId AS INT),
                TotalAmount = CAST(o.TotalAmount AS DECIMAL(18, 2)),
                PlacedAt    = CAST(o.PlacedAt AS DATETIME2(7)),
                Notes       = CAST(o.Notes AS NVARCHAR(1024))
            FROM samples.Orders AS o
            WHERE o.UserId = u.UserId
            ORDER BY o.PlacedAt
            FOR JSON PATH
        )
    FROM samples.Users AS u
    ORDER BY u.UserId
    FOR JSON PATH, ROOT('Users');
END
GO

CREATE OR ALTER PROCEDURE samples.UserBioUpdate
    @UserId INT,
    @Bio samples.UserBioType
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE samples.Users
    SET Bio = @Bio
    WHERE UserId = @UserId;

    SELECT
        UserId = CAST(UserId AS INT),
        Bio    = CAST(Bio AS NVARCHAR(512))
    FROM samples.Users
    WHERE UserId = @UserId;
END
GO

CREATE OR ALTER PROCEDURE samples.UserContactSync
    @Contacts samples.UserContactTableType READONLY
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE u
    SET
        u.Email = c.Email,
        u.DisplayName = c.DisplayName
    FROM samples.Users AS u
    INNER JOIN @Contacts AS c ON c.UserId = u.UserId;

    DECLARE @updated INT = @@ROWCOUNT;

    SELECT
        UpdatedContacts = CAST(@updated AS INT),
        MissingContacts = (
            SELECT COUNT(*)
            FROM @Contacts AS c
            WHERE NOT EXISTS (
                SELECT 1
                FROM samples.Users AS u
                WHERE u.UserId = c.UserId
            )
        );
END
GO
