namespace Commands;

internal static class CloneLogger
{
  internal static bool DebugLog { get; set; } = false;

  internal static void Log(string message)
  {
    if (DebugLog)
    {
      Console.WriteLine(message);
    }
  }
}
