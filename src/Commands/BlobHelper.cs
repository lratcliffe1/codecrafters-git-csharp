using codecrafters_git.src.Helpers;
using codecrafters_git.src.Services;

namespace codecrafters_git.src.Commands;

public interface IBlobHelper
{
  string ReadBlobContent(string hash);
  string WriteBlobObjectFromFile(string path);
  string WriteBlobObjectFromBytes(byte[] data);
}

/// <summary>
/// blob <size>\0<content>
/// </summary>
public class BlobService(ISharedUtils sharedUtils, IGitObjectWriter gitObjectWriter) : IBlobHelper
{
  public string ReadBlobContent(string hash)
  {
    string result = sharedUtils.ReadObjectString(hash);

    return ExtractBlobContent(result);
  }

  public string WriteBlobObjectFromFile(string path)
  {
    byte[] contents = File.ReadAllBytes(path);
    return WriteBlobObjectFromBytes(contents);
  }

  public string WriteBlobObjectFromBytes(byte[] data)
  {
    return gitObjectWriter.WriteObject("blob", data);
  }

  private static string ExtractBlobContent(string output)
  {
    return output.Split("\x00", 2).Last();
  }
}