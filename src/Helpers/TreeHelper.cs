using System.IO.Compression;
using System.Text;
using Classes;

namespace Helpers;

public class TreeHelper()
{
  public static string FullTree(string hash)
  {
    string path = SharedUtils.CreateBlobPath(hash);

    byte[] data = Decompress(path);

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

    byte[] data = Decompress(path);

    List<LsTreeRow> rows = GetRows(data);

    return string.Join(
      '\n',
      rows
        .OrderBy(x => x.Name)
        .Select(x => x.Name));
  }

  public static List<LsTreeRow> GetRows(byte[] data)
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

    private static byte[] Decompress(string path)
    {
        using var fileStream = File.OpenRead(path);
        using var zlibStream = new ZLibStream(fileStream, CompressionMode.Decompress);
        using var result = new MemoryStream();
        zlibStream.CopyTo(result);
        return result.ToArray();
    }
}