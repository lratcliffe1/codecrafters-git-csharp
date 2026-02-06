using System.Text;

namespace Commands;

internal static class PackfileDeframer
{
  internal static byte[] ExtractPackData(byte[] rawData)
  {
    int rawPackOffset = FindPattern(rawData, Encoding.ASCII.GetBytes("PACK"));

    return rawData[rawPackOffset..];
  }

  private static int FindPattern(byte[] data, byte[] pattern)
  {
    for (int i = 0; i <= data.Length - pattern.Length; i++)
    {
      bool match = true;
      for (int j = 0; j < pattern.Length; j++)
      {
        if (data[i + j] != pattern[j]) { match = false; break; }
      }
      if (match) return i;
    }
    return -1;
  }
}
