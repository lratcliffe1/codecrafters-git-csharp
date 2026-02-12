using System.IO.Compression;

namespace codecrafters_git.src.Commands.Clone;

public static class PackfileInflater
{
  public static byte[] InflateStandardObject(byte[] rawData, int offset, long uncompressedSize, out int bytesConsumed)
  {
    using var inputStream = new MemoryStream(rawData, offset, rawData.Length - offset);
    using var countingStream = new ThrottledCountingStream(inputStream, maxReadBytes: 1);
    using var zlibStream = new ZLibStream(countingStream, CompressionMode.Decompress, leaveOpen: true);

    if (uncompressedSize > int.MaxValue)
    {
      throw new InvalidOperationException("Object too large to materialize.");
    }

    int expectedSize = (int)uncompressedSize;
    byte[] output = new byte[expectedSize];
    int totalRead = 0;

    // Read decompressed data
    while (totalRead < expectedSize)
    {
      int bytesRead = zlibStream.Read(output, totalRead, expectedSize - totalRead);
      if (bytesRead == 0)
      {
        break;
      }
      totalRead += bytesRead;
    }

    // Drain any remaining compressed data to get accurate byte count
    byte[] drainBuffer = new byte[256];
    while (zlibStream.Read(drainBuffer, 0, drainBuffer.Length) > 0) { }

    bytesConsumed = (int)countingStream.BytesRead;
    return totalRead == expectedSize ? output : output.Take(totalRead).ToArray();
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
    using var inputStream = new MemoryStream(rawData, offset, rawData.Length - offset);
    using var countingStream = new ThrottledCountingStream(inputStream, maxReadBytes: 1);
    using Stream decompressor = useZlib
      ? new ZLibStream(countingStream, CompressionMode.Decompress, leaveOpen: true)
      : new DeflateStream(countingStream, CompressionMode.Decompress, leaveOpen: true);

    using var outputBuffer = new MemoryStream();
    var progressTracker = new DeltaProgressTracker();
    bool streamEnded = false;

    // Read byte-by-byte with incremental delta parsing to avoid O(n^2) rescans.
    while (!progressTracker.IsComplete)
    {
      int byteValue = decompressor.ReadByte();
      if (byteValue == -1)
      {
        streamEnded = true;
        break;
      }

      outputBuffer.WriteByte((byte)byteValue);
      progressTracker.Advance(outputBuffer.GetBuffer(), (int)outputBuffer.Length);
    }

    // Drain remaining compressed data if stream didn't end naturally
    if (!streamEnded)
    {
      byte[] drainBuffer = new byte[256];
      while (decompressor.Read(drainBuffer, 0, drainBuffer.Length) > 0) { }
    }

    bytesConsumed = (int)countingStream.BytesRead;
    return outputBuffer.ToArray();
  }

  private sealed class DeltaProgressTracker
  {
    private int dataIndex;
    private bool sourceSizeRead;
    private bool targetSizeRead;
    private long targetSize;
    private long bytesProduced;

    public bool IsComplete { get; private set; }

    public void Advance(byte[] data, int length)
    {
      if (IsComplete)
      {
        return;
      }

      if (!sourceSizeRead && !TryReadVarLenSize(data, length, ref dataIndex, out _))
      {
        return;
      }
      sourceSizeRead = true;

      if (!targetSizeRead && !TryReadVarLenSize(data, length, ref dataIndex, out targetSize))
      {
        return;
      }
      targetSizeRead = true;

      while (dataIndex < length && bytesProduced < targetSize)
      {
        int commandIndex = dataIndex;
        byte command = data[commandIndex];

        if ((command & PackfileConstants.COPY_COMMAND_FLAG) != 0)
        {
          if (!TryConsumeCopyCommand(data, length, commandIndex, out int nextIndex, out int copySize))
          {
            return;
          }

          dataIndex = nextIndex;
          bytesProduced += copySize;
        }
        else
        {
          if (!TryConsumeAddCommand(data, length, commandIndex, out int nextIndex, out int addSize))
          {
            return;
          }

          dataIndex = nextIndex;
          bytesProduced += addSize;
        }
      }

      IsComplete = bytesProduced == targetSize;
    }

    private static bool TryReadVarLenSize(byte[] data, int length, ref int index, out long size)
    {
      size = 0;
      int bitShift = 0;
      int cursor = index;

      while (cursor < length)
      {
        byte currentByte = data[cursor++];
        size |= (long)(currentByte & PackfileConstants.VAR_LEN_VALUE_MASK) << bitShift;

        if ((currentByte & PackfileConstants.VAR_LEN_CONTINUATION_FLAG) == 0)
        {
          index = cursor;
          return true;
        }

        bitShift += 7;
      }

      return false;
    }

    private static bool TryConsumeCopyCommand(byte[] data, int length, int commandIndex, out int nextIndex, out int copySize)
    {
      nextIndex = commandIndex;
      copySize = 0;
      byte command = data[commandIndex];
      int cursor = commandIndex + 1;

      // Consume offset bytes
      if ((command & PackfileConstants.COPY_OFFSET_BYTE_0) != 0) { if (cursor >= length) return false; cursor++; }
      if ((command & PackfileConstants.COPY_OFFSET_BYTE_1) != 0) { if (cursor >= length) return false; cursor++; }
      if ((command & PackfileConstants.COPY_OFFSET_BYTE_2) != 0) { if (cursor >= length) return false; cursor++; }
      if ((command & PackfileConstants.COPY_OFFSET_BYTE_3) != 0) { if (cursor >= length) return false; cursor++; }

      // Consume size bytes
      if ((command & PackfileConstants.COPY_SIZE_BYTE_0) != 0) { if (cursor >= length) return false; copySize |= data[cursor++]; }
      if ((command & PackfileConstants.COPY_SIZE_BYTE_1) != 0) { if (cursor >= length) return false; copySize |= data[cursor++] << 8; }
      if ((command & PackfileConstants.COPY_SIZE_BYTE_2) != 0) { if (cursor >= length) return false; copySize |= data[cursor++] << 16; }

      if (copySize == 0)
      {
        copySize = PackfileConstants.DEFAULT_COPY_SIZE;
      }

      nextIndex = cursor;
      return true;
    }

    private static bool TryConsumeAddCommand(byte[] data, int length, int commandIndex, out int nextIndex, out int addSize)
    {
      nextIndex = commandIndex;
      addSize = data[commandIndex] & PackfileConstants.ADD_SIZE_MASK;
      int commandLength = 1 + addSize;

      if (commandIndex + commandLength > length)
      {
        return false;
      }

      nextIndex = commandIndex + commandLength;
      return true;
    }
  }

  /// <summary>
  /// Wrapper stream that limits read size and counts bytes read.
  /// Used to accurately track how many compressed bytes were consumed.
  /// </summary>
  private class ThrottledCountingStream(Stream innerStream, int maxReadBytes) : Stream
  {
    private readonly Stream innerStream = innerStream;
    private readonly int maxReadBytes = Math.Max(1, maxReadBytes);

    public long BytesRead { get; private set; }

    public override bool CanRead => innerStream.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => innerStream.Length;
    public override long Position
    {
      get => innerStream.Position;
      set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
      int bytesRead = innerStream.Read(buffer, offset, Math.Min(count, maxReadBytes));
      BytesRead += bytesRead;
      return bytesRead;
    }

    public override int ReadByte()
    {
      int byteValue = innerStream.ReadByte();
      if (byteValue != -1)
      {
        BytesRead++;
      }
      return byteValue;
    }

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
  }
}
