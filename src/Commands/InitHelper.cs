using codecrafters_git.src.Services;

namespace codecrafters_git.src.Commands;

public interface IInitHelper
{
  string Init(string? directory = null);
}

public class InitHelper(
  IRepository repository,
  IGitRefHelper gitRefHelper) : IInitHelper
{
  public string Init(string? directory = null)
  {
    if (!string.IsNullOrWhiteSpace(directory))
    {
      repository.UseRepositoryRoot(directory);
    }

    Directory.CreateDirectory(repository.GitDirectory);
    Directory.CreateDirectory(repository.ObjectsDirectory);
    Directory.CreateDirectory(repository.ResolveRefPath("refs"));
    gitRefHelper.WriteHead("refs/heads/main");

    return $"Initialized git directory: {repository.GitDirectory}";
  }
}