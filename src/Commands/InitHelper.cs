namespace codecrafters_git.src.Commands;

public interface IInitHelper
{
  string Init(string? directory = null);
}

public class InitService : IInitHelper
{
  public string Init(string? directory = null)
  {
    Directory.CreateDirectory($"{directory}.git");
    Directory.CreateDirectory($"{directory}.git/objects");
    Directory.CreateDirectory($"{directory}.git/refs");
    File.WriteAllText($"{directory}.git/HEAD", "ref: refs/heads/main\n");

    return $"Initialized git directory: {directory}";
  }
}