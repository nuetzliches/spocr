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
