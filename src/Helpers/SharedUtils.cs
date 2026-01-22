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

  public static byte[] ReadZLibFileToBytes(string path)
  {
    using var fileStream = File.OpenRead(path);
    using var zlibStream = new ZLibStream(fileStream, CompressionMode.Decompress);
    using var result = new MemoryStream();
    zlibStream.CopyTo(result);
    return result.ToArray();
  }

  public static string ReadZLibFileToString(string path)
  {
    byte[] decompressedBytes = ReadZLibFileToBytes(path);
    return Encoding.UTF8.GetString(decompressedBytes);
  }

  public static long ComputeFileSize(string path)
  {
    FileInfo fileInfo = new FileInfo(path);
    long bytes = fileInfo.Length;
    return bytes;
  }

  public static string ReadFileContent(string path)
  {
    return File.ReadAllText(path, Encoding.UTF8);
  }

  public static string FormatBlobInput(string type, string contents, long size)
  {
    return $"{type} {size}\x00{contents}";
  }

  public static string CreateBlobHash(string contents)
  {
    byte[] inputBytes = Encoding.UTF8.GetBytes(contents);
    byte[] hashBytes = SHA1.HashData(inputBytes);
    return Convert.ToHexString(hashBytes).ToLower();
  }

  public static void SaveBlobContent(string contents, string path)
  {
    string? directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(directory))
      Directory.CreateDirectory(directory);

    byte[] inputBytes = Encoding.UTF8.GetBytes(contents);

    using var fileStream = File.Create(path);
    using var zlibStream = new ZLibStream(fileStream, CompressionLevel.Optimal);
    zlibStream.Write(inputBytes, 0, inputBytes.Length);
  }

  public static string CreateBlobHash(byte[] data)
  {
    byte[] hashBytes = SHA1.HashData(data);
    return Convert.ToHexString(hashBytes).ToLower();
  }

  public static void SaveBlobContent(byte[] data, string path)
  {
    // Git requires Zlib compression
    byte[] compressed = ZlibCompress(data);

    string directory = Path.GetDirectoryName(path);
    if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

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