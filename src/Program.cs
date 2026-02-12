using Microsoft.Extensions.DependencyInjection;
using codecrafters_git.src;
using codecrafters_git.src.Services;

// Set up dependency injection
var services = new ServiceCollection();

services.AddGitServices();

var serviceProvider = services.BuildServiceProvider();

// Execute command - create a scope for scoped services
using var scope = serviceProvider.CreateScope();
var commandHandler = scope.ServiceProvider.GetRequiredService<IGitCommandHandler>();
await commandHandler.ExecuteAsync(args);
