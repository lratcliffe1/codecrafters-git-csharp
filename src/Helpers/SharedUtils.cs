using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Helpers;

public class SharedUtils()
{
  public static string CreateBlobPath(string hash)
  {
    return $".git/objects/{hash[0..2]}/{hash[2..]}";
  }

  public static string ReadZLibFileToString(string path)
  {
    byte[] decompressedBytes = ReadZLibFileToBytes(path);
    return Encoding.UTF8.GetString(decompressedBytes);
  }

  public static byte[] ReadZLibFileToBytes(string path)
  {
    using var fileStream = File.OpenRead(path);
    using var zlibStream = new ZLibStream(fileStream, CompressionMode.Decompress);
    using var result = new MemoryStream();
    zlibStream.CopyTo(result);
    return result.ToArray();
  }

  public static byte[] AddHeaderString(byte[] contents, string type)
  {
    string headerString = $"{type} {contents.Length}\0";
    byte[] header = Encoding.UTF8.GetBytes(headerString);

    return header.Concat(contents).ToArray();
  }

  public static string CreateBlobHash(byte[] data)
  {
    byte[] hashBytes = SHA1.HashData(data);
    return Convert.ToHexString(hashBytes).ToLower();
  }

  public static void SaveBlobContent(byte[] data, string path)
  {
    byte[] compressed = ZlibCompress(data);

    string? directory = Path.GetDirectoryName(path);
    if (directory != null && !Directory.Exists(directory))
      Directory.CreateDirectory(directory);

    File.WriteAllBytes(path, compressed);
  }

  public static byte[] ZlibCompress(byte[] input)
  {
    using MemoryStream outputStream = new MemoryStream();
    using (ZLibStream zLibStream = new ZLibStream(outputStream, CompressionLevel.Optimal))
    {
      zLibStream.Write(input, 0, input.Length);
    }
    return outputStream.ToArray();
  }
}