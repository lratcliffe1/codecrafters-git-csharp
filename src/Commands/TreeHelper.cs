using System.Text;
using codecrafters_git.src.Commands.Clone;
using codecrafters_git.src.Helpers;
using codecrafters_git.src.Models;
using codecrafters_git.src.Services;

namespace codecrafters_git.src.Commands;

public interface ITreeHelper
{
  string ListTree(string hash);
  string ListTreeNameOnly(string hash);
  string WriteTreeObject(string path);
  void CheckoutWorkingTree(IObjectStore objectStore, string targetDirectory, string commitHash);
  List<LsTreeRow> ParseTreeRows(byte[] data, bool includesHeader);
}

/// <summary>
/// tree <size>\0<mode> <name>\0<20_byte_sha><mode> <name>\0<20_byte_sha>
/// 
/// tree <size>\0
/// <mode> <name>\0<20_byte_sha>
/// <mode> <name>\0<20_byte_sha>
/// </summary>
public class TreeService(ISharedUtils sharedUtils, IBlobHelper blobHelper, IGitObjectWriter gitObjectWriter) : ITreeHelper
{
  private const string DirectoryMode = "40000";
  private const string FileMode = "100644";

  #region Public Interface Methods

  public string ListTree(string hash)
  {
    List<LsTreeRow> rows = ReadTreeRowsFromHash(hash);
    return FormatTreeRows(rows, x => $"{x.Mode} {x.ModeName} {x.Hash} \t {x.Name}");
  }

  public string ListTreeNameOnly(string hash)
  {
    List<LsTreeRow> rows = ReadTreeRowsFromHash(hash);
    return FormatTreeRows(rows, x => x.Name);
  }

  public string WriteTreeObject(string path)
  {
    List<LsTreeRow> rows = BuildTreeRows(path);
    byte[] body = BuildTreeObjectBytes(rows);
    return gitObjectWriter.WriteObject("tree", body);
  }

  public List<LsTreeRow> ParseTreeRows(byte[] data, bool includesHeader)
  {
    int startIndex = includesHeader ? GetHeaderEndIndex(data) : 0;
    if (startIndex < 0) return [];
    return ParseRowsFromIndex(data, startIndex);
  }

  public void CheckoutWorkingTree(IObjectStore objectStore, string targetDirectory, string commitHash)
  {
    if (!objectStore.TryGetObjectByHash(commitHash, out var commitObj))
    {
      return;
    }

    string? treeHash = GetTreeHashFromCommit(commitObj.Data);
    if (string.IsNullOrWhiteSpace(treeHash))
    {
      return;
    }

    CheckoutTree(objectStore, treeHash.Trim(), targetDirectory);
  }

  #endregion

  #region Private Helper Methods

  #region Helpers for ListTree methods

  private List<LsTreeRow> ReadTreeRowsFromHash(string hash)
  {
    byte[] data = sharedUtils.ReadObjectBytes(hash);
    return ParseTreeRows(data, includesHeader: true);
  }

  private static string FormatTreeRows(List<LsTreeRow> rows, Func<LsTreeRow, string> func)
  {
    return string.Join('\n', rows.OrderBy(x => x.Name).Select(func));
  }

  #endregion

  #region Helpers for WriteTreeObject

  private List<LsTreeRow> BuildTreeRows(string path)
  {
    List<LsTreeRow> rows = [];

    // 1. Handle Subdirectories
    string[] folders = Directory.GetDirectories(path);
    foreach (var folderPath in folders)
    {
      string folderName = Path.GetFileName(folderPath);
      if (folderName == ".git") continue;

      string folderHash = WriteTreeObject(folderPath);
      rows.Add(new LsTreeRow(DirectoryMode, folderHash, folderName));
    }

    // 2. Handle Files
    string[] files = Directory.GetFiles(path);
    foreach (var filePath in files)
    {
      string fileName = Path.GetFileName(filePath);
      string fileHash = blobHelper.WriteBlobObjectFromFile(filePath);
      rows.Add(new LsTreeRow(FileMode, fileHash, fileName));
    }

    return rows;
  }

  private static byte[] BuildTreeObjectBytes(IEnumerable<LsTreeRow> rows)
  {
    using var ms = new MemoryStream();
    foreach (var row in rows.OrderBy(x => x.Name))
    {
      byte[] prefix = Encoding.UTF8.GetBytes($"{row.Mode} {row.Name}\0");
      ms.Write(prefix, 0, prefix.Length);

      byte[] hashBytes = Convert.FromHexString(row.Hash);
      ms.Write(hashBytes, 0, hashBytes.Length);
    }

    return ms.ToArray();
  }

  #endregion

  #region Helpers for ParseTreeRows

  private static List<LsTreeRow> ParseRowsFromIndex(byte[] data, int startIndex)
  {
    List<LsTreeRow> result = [];
    int i = startIndex;
    const int HashByteLength = 20;

    while (i < data.Length)
    {
      int spaceIndex = Array.IndexOf(data, (byte)' ', i);
      string mode = Encoding.UTF8.GetString(data, i, spaceIndex - i);

      int nullIndex = Array.IndexOf(data, (byte)0, spaceIndex + 1);
      string name = Encoding.UTF8.GetString(data, spaceIndex + 1, nullIndex - (spaceIndex + 1));

      byte[] hashBytes = data[(nullIndex + 1)..(nullIndex + 1 + HashByteLength)];
      string hexHash = Convert.ToHexString(hashBytes).ToLower();

      result.Add(new LsTreeRow(mode.PadLeft(6, '0'), hexHash, name));

      i = nullIndex + 21;
    }

    return result;
  }

  private static int GetHeaderEndIndex(byte[] data)
  {
    int headerNullIndex = Array.IndexOf(data, (byte)0);
    return headerNullIndex < 0 ? -1 : headerNullIndex + 1;
  }

  #endregion

  #region Helpers for CheckoutWorkingTree

  private void CheckoutTree(IObjectStore objectStore, string treeHash, string targetDirectory)
  {
    if (!objectStore.TryGetObjectByHash(treeHash, out var treeObj))
    {
      return;
    }

    var rows = ParseTreeRows(treeObj.Data, includesHeader: false);
    foreach (var row in rows)
    {
      string path = Path.Combine(targetDirectory, row.Name);
      if (row.ModeName == "tree")
      {
        Directory.CreateDirectory(path);
        CheckoutTree(objectStore, row.Hash, path);
      }
      else
      {
        if (objectStore.TryGetObjectByHash(row.Hash, out var blobObj))
        {
          Directory.CreateDirectory(Path.GetDirectoryName(path) ?? targetDirectory);
          File.WriteAllBytes(path, blobObj.Data);
        }
      }
    }
  }

  private static string? GetTreeHashFromCommit(byte[] commitData)
  {
    string commitText = Encoding.UTF8.GetString(commitData);
    return commitText
      .Split('\n')
      .FirstOrDefault(line => line.StartsWith("tree ", StringComparison.Ordinal))?
      .Split(' ', 2)
      .Last()
      ?.Trim();
  }

  #endregion

  #endregion
}