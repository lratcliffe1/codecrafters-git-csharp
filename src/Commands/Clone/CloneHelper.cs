using Helpers;

namespace Commands;

public class CloneHelper()
{
  private static readonly HttpClient client = new HttpClient();
  private static bool DebugLog = false;

  public static async Task<string> Clone(string repositoryUrl, string targetDirectory)
  {
    CloneLogger.DebugLog = DebugLog;
    CreateDirectory(targetDirectory);

    var protocol = new GitProtocolClient(client);
    (string sha1, string headRef) = await protocol.DiscoverReferences(repositoryUrl);

    byte[] packFile = await protocol.FetchPackfile(repositoryUrl, sha1);
    byte[] packData = PackfileDeframer.ExtractPackData(packFile);

    string originalCwd = Directory.GetCurrentDirectory();
    string workTreeRoot = Path.Combine(originalCwd, targetDirectory);
    Directory.SetCurrentDirectory(targetDirectory);
    try
    {
      var objectStore = new ObjectStore();
      var parser = new PackfileParser(objectStore);
      parser.ParseAllObjects(packData);
      WriteHeadAndRef(headRef, sha1);
      var checkout = new WorkingTreeCheckout(objectStore);
      checkout.CheckoutWorkingTree(workTreeRoot, sha1);
    }
    finally
    {
      Directory.SetCurrentDirectory(originalCwd);
    }

    return "";
  }

  private static void CreateDirectory(string targetDirectory)
  {
    InitHelper.Init(targetDirectory + "/");
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
