-- Simple CTE test to verify type propagation
CREATE PROCEDURE [test].[CteTypeTest]
AS
BEGIN
    WITH TestCTE AS (
        SELECT 
            ClaimId,
            ClaimValue,
            DisplayName
        FROM [identity].[Claim]
    )
    SELECT 
        c.ClaimId AS 'id',
        c.ClaimValue AS 'value',
        c.DisplayName AS 'displayName'
    FROM TestCTE AS c
    FOR JSON PATH
END