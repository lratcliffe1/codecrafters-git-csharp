using Helpers;

if (args is not [string command, ..])
{
  Console.WriteLine("Please provide a command.");
  return;
}

try
{
  string output = command switch
  {
    "init" => InitHelper.Init(),

    "cat-file" when InputValidator.ValidateCatFileInput(args)
        => BlobHelper.ReadBlob(args[2]),

    "hash-object" when InputValidator.ValidateHashObjectInput(args)
        => BlobHelper.CreateBlobFromFile(args[2]),

    "ls-tree" when InputValidator.ValidateLsTreeInput(args)
        => args.Length == 3
          ? TreeHelper.NameOnlyTree(args[2])
          : TreeHelper.FullTree(args[1]),

    "write-tree" when InputValidator.ValidateWriteTreeInput(args)
        => TreeHelper.CreateTree(Directory.GetCurrentDirectory()),

    _ => throw new ArgumentException($"Unknown or invalid command: {command}")
  };

  if (!string.IsNullOrEmpty(output))
    Console.Write(output);
}
catch (ArgumentException ex)
{
  Console.WriteLine(ex.Message);
}
