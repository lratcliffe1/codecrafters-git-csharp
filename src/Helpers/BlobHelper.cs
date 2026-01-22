namespace Helpers;

// blob <size>\0<content>
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
    long fileSize = SharedUtils.ComputeFileSize(path);

    string contents = SharedUtils.ReadFileContent(path);

    contents = SharedUtils.FormatBlobInput("blob", contents, fileSize);

    string hash = SharedUtils.CreateBlobHash(contents);

    string blobPath = SharedUtils.CreateBlobPath(hash);

    SharedUtils.SaveBlobContent(contents, blobPath);

    return hash;
  }

  private static string FormatBlobOutput(string output)
  {
    return output.Split("\x00", 2).Last();
  }
}