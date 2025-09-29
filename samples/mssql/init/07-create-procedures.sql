USE SpocRSample;
GO

CREATE OR ALTER PROCEDURE samples.UserList
AS
BEGIN
    SET NOCOUNT ON;

    SELECT UserId, Email, DisplayName, CreatedAt, Bio
    FROM samples.Users
    ORDER BY DisplayName;
END
GO

CREATE OR ALTER PROCEDURE samples.UserFind
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT UserId, Email, DisplayName, CreatedAt, Bio
    FROM samples.Users
    WHERE UserId = @UserId;
END
GO

CREATE OR ALTER PROCEDURE samples.OrderList1
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        u.UserId,
        u.DisplayName,
        u.Email,
        o.OrderId,
        o.TotalAmount,
        o.PlacedAt,
        o.Notes
    FROM samples.Users AS u
        LEFT JOIN samples.Orders AS o ON o.UserId = u.UserId
    ORDER BY u.UserId, o.PlacedAt
    FOR JSON PATH, ROOT('OrderSummaries');

END
GO

CREATE OR ALTER PROCEDURE samples.OrderList2
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        u.UserId,
        u.DisplayName,
        u.Email,
        o.OrderId,
        o.TotalAmount,
        o.PlacedAt,
        o.Notes
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
        u.UserId,
        u.DisplayName,
        u.Email,
        u.CreatedAt,
        u.Bio
    FROM samples.Users AS u
    WHERE u.UserId = @UserId;

    SELECT
        o.OrderId,
        o.TotalAmount,
        o.PlacedAt,
        o.Notes
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
        u.UserId,
        u.DisplayName,
        u.Email,
        Orders = (
            SELECT
                o.OrderId,
                o.TotalAmount,
                o.PlacedAt,
                o.Notes
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

    SELECT UserId, Bio
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
        UpdatedContacts = @updated,
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
