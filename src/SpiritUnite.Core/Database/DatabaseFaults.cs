using System.Net.Sockets;
using MySqlConnector;

namespace SpiritUnite.Database;

public static class DatabaseFaults
{
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
        or MySqlErrorCode.QueryInterrupted
        or MySqlErrorCode.ServerShutdown;
}
