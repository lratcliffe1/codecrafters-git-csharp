using Helpers;

if (args.Length < 1)
{
  Console.WriteLine("Please provide a command.");
  return;
}

string command = args[0];

if (command == "init")
{
  InitHelper.Init();
}
else if (command == "cat-file")
{
  if (!InputValidator.ValidateCatFileInput(args))
    throw new ArgumentException($"Invalid command {command}");

  string blob = BlobHelper.ReadBlob(args[2]);

  Console.Write(blob);
}
else if (command == "hash-object")
{
  if (!InputValidator.ValidateHashObjectInput(args))
    throw new ArgumentException($"Invalid command {command}");

  string hash = BlobHelper.CreateBlobFromFile(args[2]);

  Console.WriteLine(hash);
}
else if (command == "ls-tree")
{
  if (!InputValidator.ValidateLsTreeInput(args))
    throw new ArgumentException($"Invalid command {command}");

  string output;
  if (args.Length == 3)
    output = TreeHelper.NameOnlyTree(args[2]);
  else
    output = TreeHelper.FullTree(args[1]);

  Console.WriteLine(output);
}
else if (command == "write-tree")
{
  if (!InputValidator.ValidateWriteTreeInput(args))
    throw new ArgumentException($"Invalid command {command}");

  string hash = TreeHelper.CreateTree(Directory.GetCurrentDirectory());

  Console.WriteLine(hash);
}
else
{
  throw new ArgumentException($"Unknown command {command}");
}