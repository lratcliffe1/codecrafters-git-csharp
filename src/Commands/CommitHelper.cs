using System.Text;
using codecrafters_git.src.Services;

namespace codecrafters_git.src.Commands;

public interface ICommitHelper
{
  string Commit(string treeHash, string parentHash, string message);
}

/// <summary>
/// commit <size>\0tree <tree_sha>
/// parent <parent_sha>
/// author <name> <<email>> <timestamp> <timezone>
/// committer <name> <<email>> <timestamp> <timezone>
///
/// <commit message>
/// </summary>
public class CommitService(IGitObjectWriter gitObjectWriter) : ICommitHelper
{
  public string Commit(string treeHash, string parentHash, string message)
  {
    string timestampAndTimezone = GetFormattedTimestamp();
    string author = GetFormattedAuthor();
    byte[] contents = CreateCommitBody(parentHash, author, timestampAndTimezone, message);

    return gitObjectWriter.WriteObject("commit", contents, $"tree {treeHash}\n");
  }

  private static string GetFormattedTimestamp()
  {
    DateTimeOffset now = DateTimeOffset.Now;

    long secondsSinceEpoch = now.ToUnixTimeSeconds();

    string timezoneOffset = now.ToString("zzz").Replace(":", "");

    return $"{secondsSinceEpoch} {timezoneOffset}";
  }

  private static string GetFormattedAuthor()
  {
    return "Liam Ratcliffe <liam.ratcliffe@email.com>";
  }

  private static byte[] CreateCommitBody(string parentHash, string author, string timestampAndTimezone, string commitMessage)
  {
    string body = $"parent {parentHash}\n" +
      $"author {author} {timestampAndTimezone}\n" +
      $"committer {author} {timestampAndTimezone}\n" +
      "\n" +
      $"{commitMessage}\n";

    return Encoding.UTF8.GetBytes(body);
  }
}