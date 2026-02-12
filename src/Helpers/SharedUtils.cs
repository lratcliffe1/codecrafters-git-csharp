using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace codecrafters_git.src.Helpers;

public interface ISharedUtils
{
  string CreateObjectPath(string hash);
  byte[] ReadObjectBytes(string hash);
  string ReadObjectString(string hash);
  byte[] AddHeaderString(byte[] contents, string type, string? extraContent = "");
  string CreateObjectHash(byte[] data);
  void SaveObjectContent(byte[] data, string path);
  (string typeName, byte[] body) ParseObjectHeader(byte[] fullObject);
}

public class SharedUtilsService : ISharedUtils
{
  public string CreateObjectPath(string hash)
  {
    return $".git/objects/{hash[0..2]}/{hash[2..]}";
  }

  public byte[] ReadObjectBytes(string hash)
  {
    string path = CreateObjectPath(hash);
    return ReadZLibFileToBytes(path);
  }

  public string ReadObjectString(string hash)
  {
    string path = CreateObjectPath(hash);
    return ReadZLibFileToString(path);
  }

  private static byte[] ReadZLibFileToBytes(string path)
  {
    using var fileStream = File.OpenRead(path);
    using var zlibStream = new ZLibStream(fileStream, CompressionMode.Decompress);
    using var result = new MemoryStream();
    zlibStream.CopyTo(result);
    return result.ToArray();
  }

  private static string ReadZLibFileToString(string path)
  {
    byte[] decompressedBytes = ReadZLibFileToBytes(path);
    return Encoding.UTF8.GetString(decompressedBytes);
  }

  public byte[] AddHeaderString(byte[] contents, string type, string? extraContent = "")
  {
    string headerString = $"{type} {contents.Length}\0{extraContent}";
    byte[] header = Encoding.UTF8.GetBytes(headerString);

    return header.Concat(contents).ToArray();
  }

  public string CreateObjectHash(byte[] data)
  {
    byte[] hashBytes = SHA1.HashData(data);
    return Convert.ToHexString(hashBytes).ToLower();
  }

  public void SaveObjectContent(byte[] data, string path)
  {
    byte[] compressed = ZlibCompress(data);

    string? directory = Path.GetDirectoryName(path);
    if (directory != null && !Directory.Exists(directory))
      Directory.CreateDirectory(directory);

    File.WriteAllBytes(path, compressed);
  }

  private static byte[] ZlibCompress(byte[] input)
  {
    using var outputStream = new MemoryStream();
    using (var zLibStream = new ZLibStream(outputStream, CompressionLevel.Optimal))
    {
      zLibStream.Write(input, 0, input.Length);
    }
    return outputStream.ToArray();
  }

  public (string typeName, byte[] body) ParseObjectHeader(byte[] fullObject)
  {
    int spaceIndex = Array.IndexOf(fullObject, (byte)' ');
    int nullIndex = Array.IndexOf(fullObject, (byte)0, spaceIndex + 1);

    if (spaceIndex < 0 || nullIndex < 0)
    {
      throw new InvalidDataException("Invalid object format: missing header");
    }

    string typeName = Encoding.UTF8.GetString(fullObject, 0, spaceIndex);
    byte[] body = fullObject[(nullIndex + 1)..];

    return (typeName, body);
  }
}