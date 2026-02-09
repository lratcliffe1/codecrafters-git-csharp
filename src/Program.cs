using Commands;
using Helpers;

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
      Console.Write(InitHelper.Init());
      break;

    case "cat-file" when InputValidator.ValidateCatFileInput(args):
      Console.Write(BlobHelper.ReadBlobContent(args[2]));
      break;

    case "hash-object" when InputValidator.ValidateHashObjectInput(args):
      Console.WriteLine(BlobHelper.WriteBlobObjectFromFile(args[2]));
      break;

    case "ls-tree" when InputValidator.ValidateLsTreeInput(args):
      Console.WriteLine(args.Length == 3 ? TreeHelper.ListTreeNameOnly(args[2]) : TreeHelper.ListTree(args[1]));
      break;

    case "write-tree" when InputValidator.ValidateWriteTreeInput(args):
      Console.WriteLine(TreeHelper.WriteTreeObject(Directory.GetCurrentDirectory()));
      break;

    case "commit-tree" when InputValidator.ValidateCommitInput(args):
      Console.WriteLine(CommitHelper.Commit(args[1], args[3], args[5]));
      break;

    case "clone" when InputValidator.ValidateCloneInput(args):
      Console.WriteLine(await CloneHelper.Clone(args[1], args[2]));
      break;

    default:
      throw new ArgumentException($"Unknown or invalid command: {command}");
  }
}
catch (ArgumentException ex)
{
  Console.WriteLine(ex.Message);
}
