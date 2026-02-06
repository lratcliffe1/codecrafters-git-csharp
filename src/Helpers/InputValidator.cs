namespace Helpers;

public class InputValidator()
{
  public static bool ValidateCatFileInput(string[] args)
  {
    EnsureArgsLength(args, 3, "git cat-file -p <hash>");
    EnsureOption(args[1], "-p", "git cat-file -p <hash>");
    EnsureSha1(args[2], "blob hash");
    return true;
  }

  public static bool ValidateHashObjectInput(string[] args)
  {
    EnsureArgsLength(args, 3, "git hash-object -w <path>");
    EnsureOption(args[1], "-w", "git hash-object -w <path>");
    EnsureFileExists(args[2], "file path");
    return true;
  }

  public static bool ValidateLsTreeInput(string[] args)
  {
    if (args.Length == 2)
    {
      EnsureSha1(args[1], "tree hash");
      return true;
    }

    if (args.Length == 3)
    {
      EnsureOption(args[1], "--name-only", "git ls-tree [--name-only] <tree-hash>");
      EnsureSha1(args[2], "tree hash");
      return true;
    }

    throw new ArgumentException("Invalid arguments. Usage: git ls-tree [--name-only] <tree-hash>");
  }

  public static bool ValidateWriteTreeInput(string[] args)
  {
    EnsureArgsLength(args, 1, "git write-tree");
    return true;
  }

  public static bool ValidateCommitInput(string[] args)
  {
    EnsureArgsLength(args, 6, "git commit-tree <tree-hash> -p <parent-hash> -m <message>");
    EnsureSha1(args[1], "tree hash");
    EnsureOption(args[2], "-p", "git commit-tree <tree-hash> -p <parent-hash> -m <message>");
    EnsureSha1(args[3], "parent hash");
    EnsureOption(args[4], "-m", "git commit-tree <tree-hash> -p <parent-hash> -m <message>");
    EnsureNonEmpty(args[5], "commit message");
    return true;
  }

  public static bool ValidateCloneInput(string[] args)
  {
    EnsureArgsLength(args, 3, "git clone <repository-url> <target-directory>");
    EnsureUrl(args[1], "repository url");
    EnsureNonEmpty(args[2], "target directory");
    EnsureDirectoryAvailable(args[2]);
    return true;
  }

  private static void EnsureArgsLength(string[] args, int expectedLength, string usage)
  {
    if (args.Length != expectedLength)
    {
      throw new ArgumentException($"Invalid arguments. Usage: {usage}");
    }
  }

  private static void EnsureOption(string actual, string expected, string usage)
  {
    if (!string.Equals(actual, expected, StringComparison.Ordinal))
    {
      throw new ArgumentException($"Invalid arguments. Usage: {usage}");
    }
  }

  private static void EnsureSha1(string value, string label)
  {
    if (value.Length != 40 || !value.All(Uri.IsHexDigit))
    {
      throw new ArgumentException($"Invalid {label}. Expected 40 hex characters.");
    }
  }

  private static void EnsureFileExists(string path, string label)
  {
    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
    {
      throw new ArgumentException($"Invalid {label}. File does not exist: {path}");
    }
  }

  private static void EnsureNonEmpty(string value, string label)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      throw new ArgumentException($"Invalid {label}. Value cannot be empty.");
    }
  }

  private static void EnsureUrl(string value, string label)
  {
    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
        (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
    {
      throw new ArgumentException($"Invalid {label}. Expected http(s) URL.");
    }
  }

  private static void EnsureDirectoryAvailable(string targetDirectory)
  {
    if (!Directory.Exists(targetDirectory))
    {
      return;
    }

    if (Directory.EnumerateFileSystemEntries(targetDirectory).Any())
    {
      throw new ArgumentException($"Target directory is not empty: {targetDirectory}");
    }
  }
}