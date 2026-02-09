using System.Text;
using Helpers;

namespace Commands;

internal sealed class ObjectStore
{
  private readonly Dictionary<string, GitObject> objectsByHash = [];

  public Dictionary<string, GitObject> ObjectsByHash => objectsByHash;

  public void StoreObject(int type, byte[] data)
  {
    string typeName = GetTypeName(type);
    byte[] headered = SharedUtils.AddHeaderString(data, typeName);
    string hash = SharedUtils.CreateBlobHash(headered);
    string blobPath = SharedUtils.CreateBlobPath(hash);
    SharedUtils.SaveBlobContent(headered, blobPath);
    objectsByHash[hash] = new GitObject(type, data);
  }

  public bool TryGetObjectByHash(string hash, out GitObject obj)
  {
    if (objectsByHash.TryGetValue(hash, out obj))
    {
      CloneLogger.Log($"Object {hash} found in memory");
      return true;
    }

    string path = SharedUtils.CreateBlobPath(hash);
    if (!File.Exists(path))
    {
      CloneLogger.Log($"Object {hash} not found on disk at {path}");
      obj = default;
      return false;
    }

    byte[] fullObject = SharedUtils.ReadObjectBytes(hash);
    int spaceIndex = Array.IndexOf(fullObject, (byte)' ');
    int nullIndex = Array.IndexOf(fullObject, (byte)0, spaceIndex + 1);
    if (spaceIndex < 0 || nullIndex < 0)
    {
      obj = default;
      return false;
    }

    string typeName = Encoding.UTF8.GetString(fullObject, 0, spaceIndex);
    int type = GetTypeFromName(typeName);
    byte[] body = fullObject[(nullIndex + 1)..];
    obj = new GitObject(type, body);
    objectsByHash[hash] = obj;

    return true;
  }

  private static int GetTypeFromName(string typeName)
  {
    return typeName switch
    {
      "commit" => (int)GitType.Commit,
      "tree" => (int)GitType.Tree,
      "blob" => (int)GitType.Blob,
      "tag" => (int)GitType.Tag,
      _ => 0
    };
  }

  private static string GetTypeName(int type) => type switch
  {
    (int)GitType.Commit => "commit",
    (int)GitType.Tree => "tree",
    (int)GitType.Blob => "blob",
    (int)GitType.Tag => "tag",
    _ => "unknown"
  };
}
