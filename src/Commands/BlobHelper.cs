using Helpers;

namespace Commands;

/// <summary>
/// blob <size>\0<content>
/// </summary>
public class BlobHelper()
{
  public static string ReadBlobContent(string hash)
  {
    string result = SharedUtils.ReadObjectString(hash);

    return ExtractBlobContent(result);
  }

  public static string WriteBlobObjectFromFile(string path)
  {
    byte[] contents = File.ReadAllBytes(path);

    byte[] fullBlobObject = SharedUtils.AddHeaderString(contents, "blob");

    string fileHash = SharedUtils.CreateBlobHash(fullBlobObject);
    string blobPath = SharedUtils.CreateBlobPath(fileHash);

    SharedUtils.SaveBlobContent(fullBlobObject, blobPath);

    return fileHash;
  }

  private static string ExtractBlobContent(string output)
  {
    return output.Split("\x00", 2).Last();
  }
}