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
      InitHelper.Init();
      break;

    case "cat-file" when InputValidator.ValidateCatFileInput(args):
      Console.Write(BlobHelper.ReadBlob(args[2]));
      break;

    case "hash-object" when InputValidator.ValidateHashObjectInput(args):
      Console.WriteLine(BlobHelper.CreateBlobFromFile(args[2]));
      break;

    case "ls-tree" when InputValidator.ValidateLsTreeInput(args):
      string output = args.Length == 3 ? TreeHelper.NameOnlyTree(args[2]) : TreeHelper.FullTree(args[1]);
      Console.WriteLine(output);
      break;

    case "write-tree" when InputValidator.ValidateWriteTreeInput(args):
      Console.WriteLine(TreeHelper.CreateTree(Directory.GetCurrentDirectory()));
      break;

    case "commit-tree" when InputValidator.ValidateCommitInput(args):
      Console.WriteLine(CommitHelper.Commit(args[1], args[3], args[5]));
      break;

    default:
      throw new ArgumentException($"Unknown or invalid command: {command}");
  }
}
catch (ArgumentException ex)
{
  Console.WriteLine(ex.Message);
}
