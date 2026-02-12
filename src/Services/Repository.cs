namespace codecrafters_git.src.Services;

public interface IRepository
{
  string WorkTreeRoot { get; }
  void UseRepositoryRoot(string workTreeRoot);
  string GitDirectory { get; }
  string ObjectsDirectory { get; }
  string HeadPath { get; }
  string CreateObjectPath(string hash);
  string ResolveRefPath(string refName);
}

public sealed class Repository : IRepository
{
  public string WorkTreeRoot { get; private set; } = Directory.GetCurrentDirectory();

  public void UseRepositoryRoot(string workTreeRoot)
  {
    if (string.IsNullOrWhiteSpace(workTreeRoot))
    {
      throw new ArgumentException("Repository root path is required.", nameof(workTreeRoot));
    }

    WorkTreeRoot = Path.GetFullPath(workTreeRoot);
  }

  public string GitDirectory => Path.Combine(WorkTreeRoot, ".git");

  public string ObjectsDirectory => Path.Combine(GitDirectory, "objects");

  public string HeadPath => Path.Combine(GitDirectory, "HEAD");

  public string CreateObjectPath(string hash)
  {
    return Path.Combine(ObjectsDirectory, hash[0..2], hash[2..]);
  }

  public string ResolveRefPath(string refName)
  {
    return Path.Combine(GitDirectory, refName.Replace('/', Path.DirectorySeparatorChar));
  }
}
