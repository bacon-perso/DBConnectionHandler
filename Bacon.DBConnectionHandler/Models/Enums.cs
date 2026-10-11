namespace Bacon.DBConnectionHandler.Models;

/// <summary>
/// The outcome of an add, update or remove on a dynamic connection
/// </summary>
public enum ConnectionChangeStatus
{
    /// <summary>The connection was added</summary>
    Added,
    /// <summary>The connection was updated</summary>
    Updated,
    /// <summary>The connection was removed</summary>
    Removed,
    /// <summary>No dynamic connection has this name</summary>
    NotFound,
    /// <summary>A static or dynamic connection already has this name</summary>
    AlreadyExists,
    /// <summary>The operation is refused (static connection, or empty name)</summary>
    NotAllowed,
    /// <summary>Unknown provider, wrong options type, or the options failed validation</summary>
    Invalid,
    /// <summary>No provider is registered for the record's provider id (e.g. AddMssqlProvider() was not called)</summary>
    UnknownProvider,
    /// <summary>The database could not be opened. Nothing was changed</summary>
    TestFailed
}