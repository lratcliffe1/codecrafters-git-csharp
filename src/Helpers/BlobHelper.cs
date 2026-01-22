namespace Helpers;

/// <summary>
/// blob <size>\0<content>
/// </summary>
public class BlobHelper()
{
  public static string ReadBlob(string hash)
  {
    string path = SharedUtils.CreateBlobPath(hash);

    string result = SharedUtils.ReadZLibFileToString(path);

    return FormatBlobOutput(result);
  }

  public static string CreateBlobFromFile(string path)
  {
    byte[] contents = File.ReadAllBytes(path);

    byte[] fullTreeObject = SharedUtils.AddHeaderString(contents, "blob");

    string hash = SharedUtils.CreateBlobHash(fullTreeObject);
    string blobPath = SharedUtils.CreateBlobPath(hash);

    SharedUtils.SaveBlobContent(fullTreeObject, blobPath);

    return hash;
  }

  private static string FormatBlobOutput(string output)
  {
    return output.Split("\x00", 2).Last();
  }
}