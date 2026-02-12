using codecrafters_git.src.Models;

namespace codecrafters_git.src.Helpers;

public static class GitTypeConverter
{
  public static string GetTypeName(GitType type)
  {
    return type switch
    {
      GitType.Commit => "commit",
      GitType.Tree => "tree",
      GitType.Blob => "blob",
      GitType.Tag => "tag",
      _ => "unknown"
    };
  }

  public static GitType GetTypeFromName(string typeName)
  {
    return typeName switch
    {
      "commit" => GitType.Commit,
      "tree" => GitType.Tree,
      "blob" => GitType.Blob,
      "tag" => GitType.Tag,
      _ => throw new ArgumentException($"Unknown git object type: {typeName}")
    };
  }
}
