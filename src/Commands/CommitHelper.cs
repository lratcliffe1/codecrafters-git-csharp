using System.Text;
using Helpers;

namespace Commands;

/// <summary>
/// commit <size>\0tree <tree_sha>
/// parent <parent_sha>
/// author <name> <<email>> <timestamp> <timezone>
/// committer <name> <<email>> <timestamp> <timezone>
///
/// <commit message>
/// </summary>
public class CommitHelper
{
  public static string Commit(string treeHash, string parentHash, string message)
  {
    string timestampAndTimezone = GetFormattedTimestamp();

    string author = GetFormattedAuthor();

    byte[] contents = CreateCommitBody(parentHash, author, timestampAndTimezone, message);

    byte[] fullCommitObject = SharedUtils.AddHeaderString(contents, "commit", $"tree {treeHash}\n");

    string commitHash = SharedUtils.CreateBlobHash(fullCommitObject);
    string commitPath = SharedUtils.CreateBlobPath(commitHash);

    SharedUtils.SaveBlobContent(fullCommitObject, commitPath);

    return commitHash;
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