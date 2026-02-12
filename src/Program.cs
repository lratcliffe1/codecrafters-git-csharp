using Microsoft.Extensions.DependencyInjection;
using codecrafters_git.src.Commands;
using codecrafters_git.src.Commands.Clone;
using codecrafters_git.src.Helpers;
using codecrafters_git.src.Services;
using codecrafters_git.src;

// Set up dependency injection
var services = new ServiceCollection();

// Register services
services.AddTransient<IInitHelper, InitService>();
services.AddTransient<IBlobHelper, BlobService>();
services.AddTransient<ITreeHelper, TreeService>();
services.AddTransient<ICommitHelper, CommitService>();
services.AddSingleton<ISharedUtils, SharedUtilsService>();
services.AddTransient<IGitObjectWriter, GitObjectWriter>();
services.AddTransient<IGitRefHelper, GitRefHelper>();
services.AddTransient<IPackfileParser, PackfileParser>();

// Register HttpClient for GitProtocolClient
services.AddHttpClient<GitProtocolClient>();
services.AddTransient<IGitProtocolClient>(sp => sp.GetRequiredService<GitProtocolClient>());

// For clone command, we need scoped services (ObjectStore should be per clone operation)
services.AddScoped<IObjectStore, ObjectStore>();
services.AddScoped<ICloneHelper, CloneService>();

// Register command handler
services.AddTransient<IGitCommandHandler, GitCommandHandler>();

var serviceProvider = services.BuildServiceProvider();

// Execute command - create a scope for scoped services
using var scope = serviceProvider.CreateScope();
var commandHandler = scope.ServiceProvider.GetRequiredService<IGitCommandHandler>();
await commandHandler.ExecuteAsync(args);
