using System.Text;
using Helpers;

namespace Commands;

public class CloneHelper()
{
  private static readonly HttpClient client = new();

  public static async Task<string> Clone(string repositoryUrl, string targetDirectory)
  {
    CreateDirectory(targetDirectory);

    // get pack file from git api
    var protocol = new GitProtocolClient(client);
    (string sha1, string headRef) = await protocol.DiscoverReferences(repositoryUrl);
    byte[] packFile = await protocol.FetchPackfile(repositoryUrl, sha1);

    byte[] packData = RemovePackHeader(packFile);

    // set work tree root
    string originalCwd = Directory.GetCurrentDirectory();
    string workTreeRoot = Path.Combine(originalCwd, targetDirectory);
    Directory.SetCurrentDirectory(targetDirectory);

    // parse pack file
    var objectStore = new ObjectStore();
    var parser = new PackfileParser(objectStore);
    parser.ParseAllObjects(packData);

    WriteHeadAndRef(headRef, sha1);

    TreeHelper.CheckoutWorkingTree(objectStore, workTreeRoot, sha1);

    Directory.SetCurrentDirectory(originalCwd);

    return "";
  }

  private static void CreateDirectory(string targetDirectory)
  {
    InitHelper.Init(targetDirectory + "/");
  }

  private static byte[] RemovePackHeader(byte[] rawData)
  {
    int rawPackOffset = FindPattern(rawData, Encoding.ASCII.GetBytes("PACK"));

    return rawData[rawPackOffset..];
  }

  private static int FindPattern(byte[] data, byte[] pattern)
  {
    for (int i = 0; i <= data.Length - pattern.Length; i++)
    {
      bool match = true;
      for (int j = 0; j < pattern.Length; j++)
      {
        if (data[i + j] != pattern[j]) { match = false; break; }
      }
      if (match) return i;
    }
    return -1;
  }

  private static void WriteHeadAndRef(string headRef, string sha)
  {
    File.WriteAllText(".git/HEAD", $"ref: {headRef}\n");
    string refPath = Path.Combine(".git", headRef.Replace('/', Path.DirectorySeparatorChar));
    string? refDir = Path.GetDirectoryName(refPath);
    if (refDir != null && !Directory.Exists(refDir))
    {
      Directory.CreateDirectory(refDir);
    }
    File.WriteAllText(refPath, $"{sha}\n");
  }
}
