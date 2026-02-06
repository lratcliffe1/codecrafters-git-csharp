using System.Text;

namespace Commands;

internal static class PackfileDeframer
{
  internal static byte[] ExtractPackData(byte[] rawData)
  {
    if (rawData.Length >= 4 &&
        rawData[0] == (byte)'P' &&
        rawData[1] == (byte)'A' &&
        rawData[2] == (byte)'C' &&
        rawData[3] == (byte)'K')
    {
      return rawData;
    }

    if (rawData.Length >= 4 && IsHexDigit((char)rawData[0]) && IsHexDigit((char)rawData[1]) &&
        IsHexDigit((char)rawData[2]) && IsHexDigit((char)rawData[3]))
    {
      using var output = new MemoryStream();
      int index = 0;

      while (index + 4 <= rawData.Length)
      {
        int length = ReadPktLineLength(rawData, index);
        if (length < 0)
        {
          CloneLogger.Log($"Deframe: invalid pkt length at index={index}");
          if (StartsWithPack(rawData, index, rawData.Length - index))
          {
            return rawData[index..];
          }
          break;
        }

        index += 4;
        if (length == 0)
        {
          continue;
        }

        int payloadLength = length - 4;
        if (payloadLength <= 0 || index + payloadLength > rawData.Length)
        {
          CloneLogger.Log($"Deframe: payloadLength={payloadLength} index={index} rawLen={rawData.Length}");
          break;
        }

        byte channel = rawData[index];
        if (channel == 1)
        {
          output.Write(rawData, index + 1, payloadLength - 1);
        }
        else if (channel != 2 && channel != 3)
        {
          output.Write(rawData, index, payloadLength);
        }

        index += payloadLength;
      }

      byte[] deframed = output.ToArray();
      CloneLogger.Log($"Deframe: rawLen={rawData.Length}, deframedLen={deframed.Length}");
      int packOffset = FindPattern(deframed, Encoding.ASCII.GetBytes("PACK"));
      if (packOffset >= 0)
      {
        return deframed[packOffset..];
      }
    }

    int rawPackOffset = FindPattern(rawData, Encoding.ASCII.GetBytes("PACK"));
    if (rawPackOffset >= 0)
    {
      return rawData[rawPackOffset..];
    }

    return Array.Empty<byte>();
  }

  private static int ReadPktLineLength(byte[] data, int index)
  {
    // Expect 4 ASCII hex digits; return -1 if invalid.
    if (index + 4 > data.Length)
    {
      return -1;
    }

    int value = 0;
    for (int i = 0; i < 4; i++)
    {
      int hex = HexValue(data[index + i]);
      if (hex < 0)
      {
        return -1;
      }
      value = (value << 4) | hex;
    }

    return value;
  }

  private static int HexValue(byte c)
  {
    if (c >= (byte)'0' && c <= (byte)'9') return c - (byte)'0';
    if (c >= (byte)'a' && c <= (byte)'f') return c - (byte)'a' + 10;
    if (c >= (byte)'A' && c <= (byte)'F') return c - (byte)'A' + 10;
    return -1;
  }

  private static bool StartsWithPack(byte[] data, int offset, int length)
  {
    return length >= 4 &&
           data[offset] == (byte)'P' &&
           data[offset + 1] == (byte)'A' &&
           data[offset + 2] == (byte)'C' &&
           data[offset + 3] == (byte)'K';
  }

  private static bool IsHexDigit(char c)
  {
    // Accept both lowercase and uppercase hex digits.
    return (c >= '0' && c <= '9') ||
           (c >= 'a' && c <= 'f') ||
           (c >= 'A' && c <= 'F');
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
