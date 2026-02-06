using System.IO.Compression;

namespace Commands;

internal sealed class PackfileInflater
{
  public static byte[] InflateStandardObject(byte[] rawData, int offset, long uncompressedSize, out int bytesConsumed)
  {
    using var ms = new MemoryStream(rawData, offset, rawData.Length - offset);
    using var counting = new ThrottledCountingStream(ms, maxReadBytes: 1);
    using var zlib = new ZLibStream(counting, CompressionMode.Decompress, leaveOpen: true);

    if (uncompressedSize > int.MaxValue)
    {
      throw new InvalidOperationException("Object too large to materialize.");
    }

    int expected = (int)uncompressedSize;
    byte[] output = new byte[expected];
    int totalRead = 0;

    while (totalRead < expected)
    {
      int read = zlib.Read(output, totalRead, expected - totalRead);
      if (read == 0)
      {
        break;
      }
      totalRead += read;
    }

    byte[] drainBuffer = new byte[256];
    while (zlib.Read(drainBuffer, 0, drainBuffer.Length) > 0) { }

    bytesConsumed = (int)counting.BytesRead;
    return totalRead == expected ? output : output.Take(totalRead).ToArray();
  }

  public static byte[] InflateDeltaObject(byte[] rawData, int offset, out int bytesConsumed)
  {
    try
    {
      return InflateDeltaWithStream(rawData, offset, useZlib: true, out bytesConsumed);
    }
    catch (InvalidDataException)
    {
      return InflateDeltaWithStream(rawData, offset, useZlib: false, out bytesConsumed);
    }
  }

  private static byte[] InflateDeltaWithStream(byte[] rawData, int offset, bool useZlib, out int bytesConsumed)
  {
    using var ms = new MemoryStream(rawData, offset, rawData.Length - offset);
    using var counting = new ThrottledCountingStream(ms, maxReadBytes: 1);
    using Stream inflater = useZlib
      ? new ZLibStream(counting, CompressionMode.Decompress, leaveOpen: true)
      : new DeflateStream(counting, CompressionMode.Decompress, leaveOpen: true);

    using var output = new MemoryStream();
    bool complete = false;
    bool ended = false;

    while (!complete)
    {
      int value = inflater.ReadByte();
      if (value == -1)
      {
        ended = true;
        break;
      }

      output.WriteByte((byte)value);
      complete = TryParseDeltaCompletion(output.GetBuffer(), (int)output.Length, out int consumedBytes) &&
                 consumedBytes == output.Length;
    }

    if (!ended)
    {
      byte[] drainBuffer = new byte[256];
      while (inflater.Read(drainBuffer, 0, drainBuffer.Length) > 0) { }
    }

    bytesConsumed = (int)counting.BytesRead;
    return output.ToArray();
  }

  private static bool TryParseDeltaCompletion(byte[] data, int length, out int consumedBytes)
  {
    consumedBytes = 0;
    int index = 0;

    if (!TryReadDeltaSize(data, length, ref index, out long _))
    {
      return false;
    }
    if (!TryReadDeltaSize(data, length, ref index, out long targetSize))
    {
      return false;
    }

    long produced = 0;
    while (index < length && produced < targetSize)
    {
      byte cmd = data[index++];
      if ((cmd & 0x80) != 0)
      {
        int copyOffset = 0;
        int copySize = 0;

        if ((cmd & 0x01) != 0)
        {
          if (index >= length) return false;
          copyOffset |= data[index++];
        }
        if ((cmd & 0x02) != 0)
        {
          if (index >= length) return false;
          copyOffset |= data[index++] << 8;
        }
        if ((cmd & 0x04) != 0)
        {
          if (index >= length) return false;
          copyOffset |= data[index++] << 16;
        }
        if ((cmd & 0x08) != 0)
        {
          if (index >= length) return false;
          copyOffset |= data[index++] << 24;
        }

        if ((cmd & 0x10) != 0)
        {
          if (index >= length) return false;
          copySize |= data[index++];
        }
        if ((cmd & 0x20) != 0)
        {
          if (index >= length) return false;
          copySize |= data[index++] << 8;
        }
        if ((cmd & 0x40) != 0)
        {
          if (index >= length) return false;
          copySize |= data[index++] << 16;
        }

        if (copySize == 0)
        {
          copySize = 0x10000;
        }

        produced += copySize;
      }
      else
      {
        int addSize = cmd & 0x7F;
        if (index + addSize > length)
        {
          return false;
        }
        index += addSize;
        produced += addSize;
      }
    }

    if (produced != targetSize)
    {
      return false;
    }

    consumedBytes = index;
    return true;
  }

  private static bool TryReadDeltaSize(byte[] data, int length, ref int index, out long size)
  {
    size = 0;
    int shift = 0;
    while (index < length)
    {
      byte b = data[index++];
      size |= (long)(b & 0x7F) << shift;
      if ((b & 0x80) == 0)
      {
        return true;
      }
      shift += 7;
    }

    return false;
  }

  private sealed class ThrottledCountingStream : Stream
  {
    private readonly Stream inner;
    private readonly int maxReadBytes;

    public long BytesRead { get; private set; }

    public ThrottledCountingStream(Stream inner, int maxReadBytes)
    {
      this.inner = inner;
      this.maxReadBytes = Math.Max(1, maxReadBytes);
    }

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position
    {
      get => inner.Position;
      set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
      int read = inner.Read(buffer, offset, Math.Min(count, maxReadBytes));
      BytesRead += read;
      return read;
    }

    public override int ReadByte()
    {
      int value = inner.ReadByte();
      if (value != -1)
      {
        BytesRead++;
      }
      return value;
    }

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
  }
}
