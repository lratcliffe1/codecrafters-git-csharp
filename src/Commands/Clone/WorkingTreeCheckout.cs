using System.Text;

namespace Commands;

internal sealed class WorkingTreeCheckout(ObjectStore objectStore)
{
  public void CheckoutWorkingTree(string targetDirectory, string commitHash)
  {
    if (!objectStore.TryGetObjectByHash(commitHash, out var commitObj))
    {
      CloneLogger.Log("Commit object not found; skipping checkout.");
      return;
    }

    CloneLogger.Log($"Checkout: commit {commitHash} found.");

    string commitText = Encoding.UTF8.GetString(commitObj.Data);
    string? treeHash = commitText
      .Split('\n')
      .FirstOrDefault(line => line.StartsWith("tree ", StringComparison.Ordinal))?
      .Split(' ', 2)
      .Last();

    if (string.IsNullOrWhiteSpace(treeHash))
    {
      CloneLogger.Log("Tree hash not found in commit; skipping checkout.");
      return;
    }

    CloneLogger.Log($"Checkout: root tree {treeHash.Trim()}");

    CheckoutTree(treeHash.Trim(), targetDirectory);
  }

  private void CheckoutTree(string treeHash, string targetDirectory)
  {
    if (!objectStore.TryGetObjectByHash(treeHash, out var treeObj))
    {
      CloneLogger.Log($"Tree lookup failed for {treeHash}. Stored? {objectStore.ObjectsByHash.ContainsKey(treeHash)}");
      CloneLogger.Log($"Tree object {treeHash} not found; skipping.");
      return;
    }

    CloneLogger.Log($"CheckoutTree: {treeHash} -> {targetDirectory}");

    byte[] data = treeObj.Data;
    int i = 0;
    while (i < data.Length)
    {
      int spaceIndex = Array.IndexOf(data, (byte)' ', i);
      string mode = Encoding.UTF8.GetString(data, i, spaceIndex - i);

      int nullIndex = Array.IndexOf(data, (byte)0, spaceIndex + 1);
      string name = Encoding.UTF8.GetString(data, spaceIndex + 1, nullIndex - (spaceIndex + 1));

      byte[] hashBytes = data[(nullIndex + 1)..(nullIndex + 21)];
      string hexHash = Convert.ToHexString(hashBytes).ToLower();

      string path = Path.Combine(targetDirectory, name);
      if (mode.StartsWith("40000", StringComparison.Ordinal))
      {
        Directory.CreateDirectory(path);
        CloneLogger.Log($"Dir: {path} (tree {hexHash})");
        CheckoutTree(hexHash, path);
      }
      else
      {
        if (objectStore.TryGetObjectByHash(hexHash, out var blobObj))
        {
          Directory.CreateDirectory(Path.GetDirectoryName(path) ?? targetDirectory);
          File.WriteAllBytes(path, blobObj.Data);
          CloneLogger.Log($"File: {path} (blob {hexHash}, {blobObj.Data.Length} bytes)");
        }
        else
        {
          CloneLogger.Log($"Missing blob {hexHash} for path {path}");
        }
      }

      i = nullIndex + 21;
    }
  }
}
