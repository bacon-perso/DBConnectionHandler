using Bacon.DBConnectionHandler.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Bacon.DBConnectionHandler.Models;

/// <summary>
/// Everything a provider hands to the builder to register one connection
/// </summary>
/// <param name="Record">The public record of the connection</param>
/// <param name="Create">Creates the service for this connection. Called each time the connection is resolved</param>
/// <param name="Test">Opens a connection to validate it. Always run by Build(). Throws if the database is unreachable</param>
/// <param name="ConfigureServices">Optional provider specific registrations (data sources, health checks)</param>
public sealed record ConnectionHandle(ConnectionRecord Record, Func<IServiceProvider, IConnectionHandlerService> Create, Action Test, Action<IServiceCollection>? ConfigureServices = null);