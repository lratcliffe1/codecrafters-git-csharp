using codecrafters_git.src.Helpers;

namespace codecrafters_git.src.Services;

public interface IGitObjectWriter
{
  string WriteObject(string typeName, byte[] data, string? extraHeaderContent = null);
}

public class GitObjectWriter(ISharedUtils sharedUtils) : IGitObjectWriter
{
  public string WriteObject(string typeName, byte[] data, string? extraHeaderContent = null)
  {
    byte[] fullObject = sharedUtils.AddHeaderString(data, typeName, extraHeaderContent ?? "");
    string hash = sharedUtils.CreateObjectHash(fullObject);
    string objectPath = sharedUtils.CreateObjectPath(hash);
    sharedUtils.SaveObjectContent(fullObject, objectPath);
    return hash;
  }
}
