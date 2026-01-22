using System.IO.Compression;
using System.Text;

namespace Helpers;

public class BlobHelper()
{
  public static string ReadBlob(string hash)
  {
    string path = CreateBlobPath(hash);

    string result = ReadBlobContent(path);

    return FormatBlodOutput(result);
  }

  private static string CreateBlobPath(string hash)
  {
    string pathBase = ".git/objects/";
    string hash0_1 = hash[0..2];
    string hash2_38 = hash[2..];

    return $"{pathBase}/{hash0_1}/{hash2_38}";
  }

  private static string ReadBlobContent(string path)
  {
    byte[] contents = File.ReadAllBytes(path);

    using var compressedStream = new MemoryStream(contents);
    using var zlibStream = new ZLibStream(compressedStream, CompressionMode.Decompress);
    using var reader = new StreamReader(zlibStream, Encoding.UTF8);
    return reader.ReadToEnd();
  }

  private static string FormatBlodOutput(string output)
  {
    return output.Split("\x00", 2).Last();
  }
}