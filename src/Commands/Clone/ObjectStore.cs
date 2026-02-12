using codecrafters_git.src.Helpers;
using codecrafters_git.src.Models;
using codecrafters_git.src.Services;

namespace codecrafters_git.src.Commands.Clone;

public interface IObjectStore
{
  Dictionary<string, GitObject> ObjectsByHash { get; }
  void StoreObject(int type, byte[] data);
  bool TryGetObjectByHash(string hash, out GitObject obj);
}

public class ObjectStore(ISharedUtils sharedUtils, IGitObjectWriter gitObjectWriter) : IObjectStore
{
  private readonly Dictionary<string, GitObject> objectsByHash = [];

  public Dictionary<string, GitObject> ObjectsByHash => objectsByHash;

  public void StoreObject(int type, byte[] data)
  {
    // Convert type enum to type name string
    GitType gitType = (GitType)type;
    string typeName = GitTypeConverter.GetTypeName(gitType);

    // Write object to disk and get its SHA-1 hash
    string objectHash = gitObjectWriter.WriteObject(typeName, data);

    // Cache in memory for fast access
    objectsByHash[objectHash] = new GitObject(type, data);
  }

  public bool TryGetObjectByHash(string hash, out GitObject obj)
  {
    // Check memory cache first (fast path)
    if (objectsByHash.TryGetValue(hash, out obj))
    {
      return true;
    }

    // Fall back to disk: check if object file exists
    string objectPath = sharedUtils.CreateObjectPath(hash);
    if (!File.Exists(objectPath))
    {
      obj = default;
      return false;
    }

    // Read object from disk and parse it
    byte[] fullObjectData = sharedUtils.ReadObjectBytes(hash);

    try
    {
      // Parse object header to extract type and body
      (string typeName, byte[] body) = sharedUtils.ParseObjectHeader(fullObjectData);
      GitType gitType = GitTypeConverter.GetTypeFromName(typeName);

      // Create object and cache it in memory
      obj = new GitObject((int)gitType, body);
      objectsByHash[hash] = obj;
      return true;
    }
    catch (ArgumentException)
    {
      // Invalid type name
      obj = default;
      return false;
    }
    catch (InvalidDataException)
    {
      // Invalid object format
      obj = default;
      return false;
    }
  }
}
