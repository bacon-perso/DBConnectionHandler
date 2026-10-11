using Bacon.DBConnectionHandler.Extensions;
using Bacon.DBConnectionHandler.Interfaces.Services;
using Bacon.DBConnectionHandler.Interfaces.Stores;
using Bacon.DBConnectionHandler.Models;
using Bacon.DBConnectionHandler.Models.Options;
using Bacon.DBConnectionHandler.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bacon.DBConnectionHandler.Tests.Fakes;

/// <summary>
/// A built service provider wired with the fake provider, so a test only describes what is different
/// </summary>
internal sealed class TestHost : IAsyncDisposable
{
    private TestHost(ServiceProvider serviceProvider, FakeConnectionProvider provider)
    {
        ServiceProvider = serviceProvider;
        Provider = provider;
    }

    public ServiceProvider ServiceProvider { get; }

    public FakeConnectionProvider Provider { get; }

    public IConnectionHandlerManager Manager => ServiceProvider.GetRequiredService<IConnectionHandlerManager>();

    public IConnectionHandlerFactory Factory => ServiceProvider.GetRequiredService<IConnectionHandlerFactory>();

    /// <summary>
    /// Builds a host. Everything is optional
    /// </summary>
    /// <param name="configure">Adds static connections or changes the builder before Build()</param>
    /// <param name="store">A store to use instead of the default in-memory one</param>
    /// <param name="timeProvider">A clock to use instead of the system one</param>
    /// <param name="provider">The fake provider. A new one is created if null</param>
    public static TestHost Create(Action<ConnectionHandlerBuilder>? configure = null, IConnectionStore? store = null, TimeProvider? timeProvider = null, FakeConnectionProvider? provider = null)
    {
        FakeConnectionProvider fakeConnectionProvider = provider ?? new FakeConnectionProvider();
        ServiceCollection services = new();

        // Registered before AddConnectionHandler(), which only adds the defaults when nothing is there yet
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        if (timeProvider is not null)
        {
            services.AddSingleton(timeProvider);
        }

        if (store is not null)
        {
            services.AddSingleton(store);
        }

        ConnectionHandlerBuilder builder = services.AddConnectionHandler();
        services.AddSingleton<IConnectionProvider>(fakeConnectionProvider);
        builder.HasProviders = true;

        configure?.Invoke(builder);

        builder.Build();

        return new TestHost(services.BuildServiceProvider(), fakeConnectionProvider);
    }

    /// <summary>
    /// A record for the fake provider
    /// </summary>
    public static ConnectionRecord Record(string name, string connectionString = "fake-connection", string providerId = FakeConnectionProvider.DefaultProviderId)
    {
        return new ConnectionRecord(name, providerId, new FakeConnectionHandlerOptions { ConnectionString = connectionString });
    }

    /// <summary>
    /// A static connection that never touches a database
    /// </summary>
    /// <param name="name">The connection name</param>
    /// <param name="test">What opening the connection at Build() does. Nothing by default</param>
    public static ConnectionHandle StaticHandle(string name, Action? test = null)
    {
        return new ConnectionHandle(
            Record(name, "static-" + name),
            _ => new FakeConnectionHandlerService("static-" + name),
            test ?? (() => { }));
    }

    /// <summary>
    /// Waits until a condition holds. Used for work the library does in the background, like retiring a service
    /// </summary>
    public static async Task<bool> WaitForAsync(Func<bool> condition, int timeoutMilliseconds = 5000)
    {
        DateTime limit = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);

        while (DateTime.UtcNow < limit)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10);
        }

        return condition();
    }

    public async ValueTask DisposeAsync()
    {
        await ServiceProvider.DisposeAsync();
    }
}
