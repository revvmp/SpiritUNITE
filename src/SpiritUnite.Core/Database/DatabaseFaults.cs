using System.Net.Sockets;
using MySqlConnector;

namespace SpiritUnite.Database;

public static class DatabaseFaults
{
    public static bool IsLockConflict(Exception exception) => exception switch
    {
        MySqlException mysql => mysql.ErrorCode is MySqlErrorCode.LockDeadlock or MySqlErrorCode.LockWaitTimeout or MySqlErrorCode.CheckRead,
        AggregateException aggregate => aggregate.InnerExceptions.Count > 0 && aggregate.InnerExceptions.All(IsLockConflict),
        _ => exception.InnerException is { } inner && IsLockConflict(inner),
    };

    public static bool IsTransient(Exception exception) => exception switch
    {
        MySqlException mysql when mysql.IsTransient || IsTransientErrorCode(mysql.ErrorCode) => true,
        TimeoutException or IOException or SocketException => true,
        AggregateException aggregate => aggregate.InnerExceptions.Any(IsTransient),
        _ => exception.InnerException is { } inner && IsTransient(inner),
    };

    private static bool IsTransientErrorCode(MySqlErrorCode errorCode) => errorCode
        is MySqlErrorCode.UnableToConnectToHost
        or MySqlErrorCode.CommandTimeoutExpired
        or MySqlErrorCode.ConnectionCountError
        or MySqlErrorCode.LockDeadlock
        or MySqlErrorCode.LockWaitTimeout
        or MySqlErrorCode.CheckRead
        or MySqlErrorCode.QueryInterrupted
        or MySqlErrorCode.ServerShutdown;
}
