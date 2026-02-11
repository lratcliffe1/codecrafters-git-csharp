namespace Commands;

public class InitHelper()
{
  public static string Init(string? directory = null)
  {
    Directory.CreateDirectory($"{directory}.git");
    Directory.CreateDirectory($"{directory}.git/objects");
    Directory.CreateDirectory($"{directory}.git/refs");
    File.WriteAllText($"{directory}.git/HEAD", "ref: refs/heads/main\n");

    return $"Initialized git directory: {directory}";
  }
}