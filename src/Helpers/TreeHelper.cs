using System.Text;
using Classes;

namespace Helpers;

// tree <size>\0
// <mode> <name>\0<20_byte_sha>
// <mode> <name>\0<20_byte_sha>
public class TreeHelper()
{
  public static string FullTree(string hash)
  {
    string path = SharedUtils.CreateBlobPath(hash);

    byte[] data = SharedUtils.ReadZLibFileToBytes(path);

    List<LsTreeRow> rows = GetRows(data);

    return string.Join(
      '\n',
      rows
        .OrderBy(x => x.Name)
        .Select(x => $"{x.Mode} {x.ModeName} {x.Hash} \t {x.Name}"));
  }

  public static string NameOnlyTree(string hash)
  {
    string path = SharedUtils.CreateBlobPath(hash);

    byte[] data = SharedUtils.ReadZLibFileToBytes(path);

    List<LsTreeRow> rows = GetRows(data);

    return string.Join(
      '\n',
      rows
        .OrderBy(x => x.Name)
        .Select(x => x.Name));
  }

  public static string CreateTree(string path)
  {
    List<LsTreeRow> rows = CreateTreeRecursive(path);

    using MemoryStream ms = new MemoryStream();
    foreach (var row in rows.OrderBy(x => x.Name))
    {
      byte[] prefix = Encoding.UTF8.GetBytes($"{row.Mode} {row.Name}\0");
      ms.Write(prefix, 0, prefix.Length);

      byte[] hashBytes = Convert.FromHexString(row.Hash);
      ms.Write(hashBytes, 0, hashBytes.Length);
    }

    byte[] body = ms.ToArray();

    string headerString = $"tree {body.Length}\0";
    byte[] header = Encoding.UTF8.GetBytes(headerString);

    byte[] fullTreeObject = header.Concat(body).ToArray();

    string hash = SharedUtils.CreateBlobHash(fullTreeObject);
    string blobPath = SharedUtils.CreateBlobPath(hash);
    SharedUtils.SaveBlobContent(fullTreeObject, blobPath);

    return hash;
  }

  private static List<LsTreeRow> CreateTreeRecursive(string path)
  {
    List<LsTreeRow> rows = new List<LsTreeRow>();

    // 1. Handle Subdirectories
    string[] folders = Directory.GetDirectories(path);
    foreach (var folderPath in folders)
    {
      string folderName = Path.GetFileName(folderPath);
      if (folderName == ".git") continue;

      string folderHash = CreateTree(folderPath);

      rows.Add(new LsTreeRow("40000", folderHash, folderName));
    }

    // 2. Handle Files
    string[] files = Directory.GetFiles(path);
    foreach (var filePath in files)
    {
      string fileName = Path.GetFileName(filePath);

      byte[] fileContent = File.ReadAllBytes(filePath);

      byte[] header = Encoding.UTF8.GetBytes($"blob {fileContent.Length}\0");
      byte[] fullBlobObject = header.Concat(fileContent).ToArray();

      string fileHash = SharedUtils.CreateBlobHash(fullBlobObject);
      string blobPath = SharedUtils.CreateBlobPath(fileHash);
      SharedUtils.SaveBlobContent(fullBlobObject, blobPath);

      rows.Add(new LsTreeRow("100644", fileHash, fileName));
    }

    return rows;
  }

  private static List<LsTreeRow> GetRows(byte[] data)
  {
    List<LsTreeRow> result = [];

    int i = Array.IndexOf(data, (byte)0) + 1;

    while (i < data.Length)
    {
      int spaceIndex = Array.IndexOf(data, (byte)' ', i);
      string mode = Encoding.UTF8.GetString(data, i, spaceIndex - i);

      int nullIndex = Array.IndexOf(data, (byte)0, spaceIndex + 1);
      string name = Encoding.UTF8.GetString(data, spaceIndex + 1, nullIndex - (spaceIndex + 1));

      byte[] hashBytes = data[(nullIndex + 1)..(nullIndex + 21)];
      string hexHash = Convert.ToHexString(hashBytes).ToLower();

      result.Add(new LsTreeRow(mode.PadLeft(6, '0'), hexHash, name));

      i = nullIndex + 21;
    }

    return result;
  }
}