namespace Bacon.DBConnectionHandler.Models.Options;

/// <summary>
/// Connection handler configurations
/// </summary>
public abstract class ConnectionHandlerOptions
{
    /// <summary>
    /// The connection string used for connection to the database
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// The health check options. If the option is null, no health will be done. (default value : null)
    /// </summary>
    public ConnectionHandlerHealthCheckOptions? ConnectionHandlerHealthCheckOptions { get; set; }

    /// <summary>
    /// Validates the connection string or other properties from the options
    /// </summary>
    /// <exception cref="ArgumentException"></exception>
    public virtual void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new ArgumentException("DB - The connection string cannot be null or empty", nameof(ConnectionString));
        }
    }
}