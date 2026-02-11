using System.Text;
using Classes;
using Helpers;

namespace Commands;

/// <summary>
/// tree <size>\0<mode> <name>\0<20_byte_sha><mode> <name>\0<20_byte_sha>
/// 
/// tree <size>\0
/// <mode> <name>\0<20_byte_sha>
/// <mode> <name>\0<20_byte_sha>
/// </summary>
public class TreeHelper()
{
  private const string DirectoryMode = "40000";
  private const string FileMode = "100644";
  private const int HashByteLength = 20;

  public static string ListTree(string hash)
  {
    List<LsTreeRow> rows = ReadTreeRowsFromHash(hash);

    return FormatTreeRows(rows, x => $"{x.Mode} {x.ModeName} {x.Hash} \t {x.Name}");
  }

  public static string ListTreeNameOnly(string hash)
  {
    List<LsTreeRow> rows = ReadTreeRowsFromHash(hash);

    return FormatTreeRows(rows, x => x.Name);
  }

  private static string FormatTreeRows(List<LsTreeRow> rows, Func<LsTreeRow, string> func)
  {
    return string.Join('\n', rows.OrderBy(x => x.Name).Select(func));
  }

  public static string WriteTreeObject(string path)
  {
    List<LsTreeRow> rows = BuildTreeRows(path);

    byte[] body = BuildTreeObjectBytes(rows);

    byte[] fullTreeObject = SharedUtils.AddHeaderString(body, "tree");

    string hash = SharedUtils.CreateBlobHash(fullTreeObject);
    string blobPath = SharedUtils.CreateBlobPath(hash);

    SharedUtils.SaveBlobContent(fullTreeObject, blobPath);

    return hash;
  }

  private static List<LsTreeRow> BuildTreeRows(string path)
  {
    List<LsTreeRow> rows = [];

    // 1. Handle Subdirectories
    string[] folders = Directory.GetDirectories(path);
    foreach (var folderPath in folders)
    {
      string folderName = Path.GetFileName(folderPath);
      if (folderName == ".git") continue;

      string folderHash = WriteTreeObject(folderPath);

      rows.Add(new LsTreeRow(DirectoryMode, folderHash, folderName));
    }

    // 2. Handle Files
    string[] files = Directory.GetFiles(path);
    foreach (var filePath in files)
    {
      string fileName = Path.GetFileName(filePath);
      string fileHash = WriteBlobObjectFromFile(filePath);

      rows.Add(new LsTreeRow(FileMode, fileHash, fileName));
    }

    return rows;
  }

  public static List<LsTreeRow> ParseTreeRows(byte[] data, bool includesHeader)
  {
    int startIndex = includesHeader ? GetHeaderEndIndex(data) : 0;
    if (startIndex < 0) return [];
    return ParseRowsFromIndex(data, startIndex);
  }

  private static List<LsTreeRow> ParseRowsFromIndex(byte[] data, int startIndex)
  {
    List<LsTreeRow> result = [];

    int i = startIndex;

    while (i < data.Length)
    {
      int spaceIndex = Array.IndexOf(data, (byte)' ', i);
      string mode = Encoding.UTF8.GetString(data, i, spaceIndex - i);

      int nullIndex = Array.IndexOf(data, (byte)0, spaceIndex + 1);
      string name = Encoding.UTF8.GetString(data, spaceIndex + 1, nullIndex - (spaceIndex + 1));

      byte[] hashBytes = data[(nullIndex + 1)..(nullIndex + 1 + HashByteLength)];
      string hexHash = Convert.ToHexString(hashBytes).ToLower();

      result.Add(new LsTreeRow(mode.PadLeft(6, '0'), hexHash, name));

      i = nullIndex + 21;
    }

    return result;
  }

  internal static void CheckoutWorkingTree(ObjectStore objectStore, string targetDirectory, string commitHash)
  {
    if (!objectStore.TryGetObjectByHash(commitHash, out var commitObj))
    {
      CloneLogger.Log("Commit object not found; skipping checkout.");
      return;
    }

    CloneLogger.Log($"Checkout: commit {commitHash} found.");

    string? treeHash = TryGetTreeHashFromCommit(commitObj.Data);
    if (string.IsNullOrWhiteSpace(treeHash))
    {
      CloneLogger.Log("Tree hash not found in commit; skipping checkout.");
      return;
    }

    CloneLogger.Log($"Checkout: root tree {treeHash.Trim()}");

    CheckoutTree(objectStore, treeHash.Trim(), targetDirectory);
  }

  private static void CheckoutTree(ObjectStore objectStore, string treeHash, string targetDirectory)
  {
    if (!objectStore.TryGetObjectByHash(treeHash, out var treeObj))
    {
      CloneLogger.Log($"Tree lookup failed for {treeHash}. Stored? {objectStore.ObjectsByHash.ContainsKey(treeHash)}");
      CloneLogger.Log($"Tree object {treeHash} not found; skipping.");
      return;
    }

    CloneLogger.Log($"CheckoutTree: {treeHash} -> {targetDirectory}");

    var rows = ParseTreeRows(treeObj.Data, includesHeader: false);
    foreach (var row in rows)
    {
      string path = Path.Combine(targetDirectory, row.Name);
      if (row.ModeName == "tree")
      {
        Directory.CreateDirectory(path);
        CloneLogger.Log($"Dir: {path} (tree {row.Hash})");
        CheckoutTree(objectStore, row.Hash, path);
      }
      else
      {
        if (objectStore.TryGetObjectByHash(row.Hash, out var blobObj))
        {
          Directory.CreateDirectory(Path.GetDirectoryName(path) ?? targetDirectory);
          File.WriteAllBytes(path, blobObj.Data);
          CloneLogger.Log($"File: {path} (blob {row.Hash}, {blobObj.Data.Length} bytes)");
        }
        else
        {
          CloneLogger.Log($"Missing blob {row.Hash} for path {path}");
        }
      }
    }
  }

  private static List<LsTreeRow> ReadTreeRowsFromHash(string hash)
  {
    byte[] data = SharedUtils.ReadObjectBytes(hash);
    return ParseTreeRows(data, includesHeader: true);
  }

  private static byte[] BuildTreeObjectBytes(IEnumerable<LsTreeRow> rows)
  {
    using var ms = new MemoryStream();
    foreach (var row in rows.OrderBy(x => x.Name))
    {
      byte[] prefix = Encoding.UTF8.GetBytes($"{row.Mode} {row.Name}\0");
      ms.Write(prefix, 0, prefix.Length);

      byte[] hashBytes = Convert.FromHexString(row.Hash);
      ms.Write(hashBytes, 0, hashBytes.Length);
    }

    return ms.ToArray();
  }

  private static string WriteBlobObjectFromFile(string path)
  {
    byte[] fileContent = File.ReadAllBytes(path);

    byte[] fullBlobObject = SharedUtils.AddHeaderString(fileContent, "blob");

    string fileHash = SharedUtils.CreateBlobHash(fullBlobObject);
    string blobPath = SharedUtils.CreateBlobPath(fileHash);

    SharedUtils.SaveBlobContent(fullBlobObject, blobPath);

    return fileHash;
  }

  private static int GetHeaderEndIndex(byte[] data)
  {
    int headerNullIndex = Array.IndexOf(data, (byte)0);
    return headerNullIndex < 0 ? -1 : headerNullIndex + 1;
  }

  private static string? TryGetTreeHashFromCommit(byte[] commitData)
  {
    string commitText = Encoding.UTF8.GetString(commitData);
    return commitText
      .Split('\n')
      .FirstOrDefault(line => line.StartsWith("tree ", StringComparison.Ordinal))?
      .Split(' ', 2)
      .Last()
      ?.Trim();
  }
}