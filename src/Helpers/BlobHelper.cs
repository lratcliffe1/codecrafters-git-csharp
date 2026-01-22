using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Helpers;

public class BlobHelper()
{
  public static string ReadBlob(string hash)
  {
    string path = SharedUtils.CreateBlobPath(hash);

    string result = ReadBlobContent(path);

    return FormatBlobOutput(result);
  }

  public static string CreateBlob(string path)
  {
    long fileSize = ComputeFileSize(path);

    string contents = ReadFileContent(path);

    contents = FormatBlobInput(contents, fileSize);

    string hash = CreateBlobHash(contents);

    string blobPath = SharedUtils.CreateBlobPath(hash);
    
    SaveBlobContent(contents, blobPath);

    return hash;
  }

  private static string ReadBlobContent(string path)
  {
    byte[] contents = File.ReadAllBytes(path);

    using var compressedStream = new MemoryStream(contents);
    using var zlibStream = new ZLibStream(compressedStream, CompressionMode.Decompress);
    using var reader = new StreamReader(zlibStream, Encoding.UTF8);
    return reader.ReadToEnd();
  }

  private static string FormatBlobOutput(string output)
  {
    return output.Split("\x00", 2).Last();
  }

  private static long ComputeFileSize(string path)
  {
    FileInfo fileInfo = new FileInfo(path);
    long bytes = fileInfo.Length;
    return bytes;
  }

  public static string ReadFileContent(string path)
  {
    return File.ReadAllText(path, Encoding.UTF8);
  }

  private static string FormatBlobInput(string contents, long size)
  {
    return $"blob {size}\x00{contents}";
  }

  private static string CreateBlobHash(string contents)
  {
    byte[] inputBytes = Encoding.UTF8.GetBytes(contents);
    byte[] hashBytes = SHA1.HashData(inputBytes);
    return Convert.ToHexString(hashBytes).ToLower(); 
  }

  private static void SaveBlobContent(string contents, string path)
  {
    string? directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(directory))
        Directory.CreateDirectory(directory);

    byte[] inputBytes = Encoding.UTF8.GetBytes(contents);

    using var fileStream = File.Create(path);
    using var zlibStream = new ZLibStream(fileStream, CompressionLevel.Optimal);
    zlibStream.Write(inputBytes, 0, inputBytes.Length);
  }
}