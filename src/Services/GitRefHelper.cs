namespace codecrafters_git.src.Services;

public interface IGitRefHelper
{
  void WriteHead(string refName);
  void WriteRef(string refName, string hash);
}

public class GitRefHelper : IGitRefHelper
{
  public void WriteHead(string refName)
  {
    File.WriteAllText(".git/HEAD", $"ref: {refName}\n");
  }

  public void WriteRef(string refName, string hash)
  {
    string refPath = Path.Combine(".git", refName.Replace('/', Path.DirectorySeparatorChar));
    string? refDir = Path.GetDirectoryName(refPath);
    if (refDir != null && !Directory.Exists(refDir))
    {
      Directory.CreateDirectory(refDir);
    }
    File.WriteAllText(refPath, $"{hash}\n");
  }
}
