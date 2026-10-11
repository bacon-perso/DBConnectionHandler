namespace Bacon.DBConnectionHandler.Models.Options;

/// <summary>
/// Settings for the dynamic connections. Static connections are not affected
/// </summary>
public sealed class DynamicConnectionOptions
{
    /// <summary>
    /// How long a node keeps a materialized connection before re-reading the store ("burst cache").
    /// Null means never. Set it when the store is shared between nodes, it bounds how stale a node can be. (default value : null)
    /// </summary>
    public TimeSpan? CacheDuration { get; set; }

    /// <summary>
    /// How long a replaced, removed or expired connection keeps working before it is disposed, so calls already running can finish.
    /// Only matters for providers that own a disposable resource (Postgres). (default value : 30 seconds)
    /// </summary>
    public TimeSpan RetireDelay { get; set; } = TimeSpan.FromSeconds(30);
}