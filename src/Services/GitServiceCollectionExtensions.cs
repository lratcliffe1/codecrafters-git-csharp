using codecrafters_git.src.Commands;
using codecrafters_git.src.Commands.Clone;
using codecrafters_git.src.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace codecrafters_git.src.Services;

public static class GitServiceCollectionExtensions
{
  public static IServiceCollection AddGitServices(this IServiceCollection services)
  {
    services.AddScoped<IRepository, Repository>();
    services.AddScoped<ISharedUtils, SharedUtils>();

    services.AddTransient<IInitHelper, InitHelper>();
    services.AddTransient<IBlobHelper, BlobHelper>();
    services.AddTransient<ITreeHelper, TreeHelper>();
    services.AddTransient<ICommitHelper, CommitHelper>();
    services.AddTransient<IGitObjectWriter, GitObjectWriter>();
    services.AddTransient<IGitRefHelper, GitRefHelper>();
    services.AddTransient<IPackfileParser, PackfileParser>();

    // Register HttpClient for GitProtocolClient
    services.AddHttpClient<IGitProtocolClient, GitProtocolClient>();

    // ObjectStore is scoped so a clone operation has isolated in-memory state.
    services.AddScoped<IObjectStore, ObjectStore>();
    services.AddScoped<ICloneHelper, CloneHelper>();

    services.AddTransient<IGitCommandHandler, GitCommandHandler>();
    return services;
  }
}
