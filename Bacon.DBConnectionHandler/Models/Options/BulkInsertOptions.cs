namespace Bacon.DBConnectionHandler.Models.Options;

/// <summary>
/// Base class of the provider specific bulk insert options.
/// Each provider package defines its own subclass. Passing the options of another provider throws an <see cref="ArgumentException"/>.
/// </summary>
public abstract class BulkInsertOptions;