namespace SpocR.Enums;

public enum ExecuteResultEnum
{
    Undefined = 0,
    Succeeded = 1,
    Aborted = -1,
    Error = -9,
    Skipped = 10,
    Exception = -99
}