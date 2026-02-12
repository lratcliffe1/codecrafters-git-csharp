using codecrafters_git.src.Commands;
using codecrafters_git.src.Commands.Clone;
using codecrafters_git.src.Helpers;

namespace codecrafters_git.src;

public interface IGitCommandHandler
{
  Task ExecuteAsync(string[] args);
}

public class GitCommandHandler(
  IInitHelper initHelper,
  IBlobHelper blobHelper,
  ITreeHelper treeHelper,
  ICommitHelper commitHelper,
  ICloneHelper cloneHelper) : IGitCommandHandler
{
  public async Task ExecuteAsync(string[] args)
  {
    if (args is not [var command, ..])
    {
      Console.WriteLine("Please provide a command.");
      return;
    }

    try
    {
      switch (command)
      {
        case "init":
          Console.Write(initHelper.Init());
          break;

        case "cat-file" when InputValidator.ValidateCatFileInput(args):
          Console.Write(blobHelper.ReadBlobContent(args[2]));
          break;

        case "hash-object" when InputValidator.ValidateHashObjectInput(args):
          Console.WriteLine(blobHelper.WriteBlobObjectFromFile(args[2]));
          break;

        case "ls-tree" when InputValidator.ValidateLsTreeInput(args):
          Console.WriteLine(args.Length == 3 ? treeHelper.ListTreeNameOnly(args[2]) : treeHelper.ListTree(args[1]));
          break;

        case "write-tree" when InputValidator.ValidateWriteTreeInput(args):
          Console.WriteLine(treeHelper.WriteTreeObject(Directory.GetCurrentDirectory()));
          break;

        case "commit-tree" when InputValidator.ValidateCommitInput(args):
          Console.WriteLine(commitHelper.Commit(args[1], args[3], args[5]));
          break;

        case "clone" when InputValidator.ValidateCloneInput(args):
          Console.WriteLine(await cloneHelper.Clone(args[1], args[2]));
          break;

        default:
          throw new ArgumentException($"Unknown or invalid command: {command}");
      }
    }
    catch (ArgumentException ex)
    {
      Console.WriteLine(ex.Message);
    }
  }
}
