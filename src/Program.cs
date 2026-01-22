using Helpers;

if (args.Length < 1)
{
    Console.WriteLine("Please provide a command.");
    return;
}

// You can use print statements as follows for debugging, they'll be visible when running tests.
// Console.Error.WriteLine("Logs from your program will appear here!");

string command = args[0];

if (command == "init")
{
    InitHelper.Init();
}
else if (command == "cat-file")
{
    string blob = BlobHelper.ReadBlob(args[2]);

    Console.Write(blob);
}
else if (command == "hash-object")
{
    string hash = BlobHelper.CreateBlob(args[2]);

    Console.WriteLine(hash);
}
else
{
    throw new ArgumentException($"Unknown command {command}");
}