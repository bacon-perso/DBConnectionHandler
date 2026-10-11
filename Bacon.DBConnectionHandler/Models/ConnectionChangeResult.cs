namespace Bacon.DBConnectionHandler.Models;

/// <summary>
/// Result of a change on a dynamic connection. Store failures are not reported here, they are thrown
/// </summary>
/// <param name="Name">The connection name</param>
/// <param name="Status">What happened</param>
/// <param name="Error">The cause, set for <see cref="ConnectionChangeStatus.Invalid"/> and <see cref="ConnectionChangeStatus.TestFailed"/></param>
public sealed record ConnectionChangeResult(string Name, ConnectionChangeStatus Status, Exception? Error = null)
{
    /// <summary>
    /// True if the change was applied
    /// </summary>
    public bool Succeeded => Status is ConnectionChangeStatus.Added or ConnectionChangeStatus.Updated or ConnectionChangeStatus.Removed;
}