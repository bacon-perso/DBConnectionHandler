using Bacon.DBConnectionHandler.Models;

namespace Bacon.DBConnectionHandler.Registries;

internal sealed class ConnectionHandlerRegistry
{
    #region Properties

    private readonly Dictionary<string, ConnectionRecord> _records = [];

    // Set by Build(). Lets the factory tell "Build() was never called" from "unknown name", now that a build may add no static connection
    public bool IsBuilt { get; private set; }

    #endregion Properties

    public IReadOnlyCollection<string> Names => _records.Keys;

    public void Add(ConnectionRecord record) => _records[record.Name] = record;

    public bool Contains(string name) => _records.ContainsKey(name);

    public void MarkBuilt() => IsBuilt = true;
}