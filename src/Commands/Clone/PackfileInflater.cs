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
    bool deltaComplete = false;
    bool streamEnded = false;

    // Read byte-by-byte, checking if we have a complete valid delta after each byte
    while (!deltaComplete)
    {
      int byteValue = decompressor.ReadByte();
      if (byteValue == -1)
      {
        streamEnded = true;
        break;
      }

      outputBuffer.WriteByte((byte)byteValue);

      // Check if we have a complete, valid delta
      deltaComplete = TryParseDeltaCompletion(outputBuffer.GetBuffer(), (int)outputBuffer.Length, out int consumedBytes) && consumedBytes == outputBuffer.Length;
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

  private static bool TryParseDeltaCompletion(byte[] data, int length, out int consumedBytes)
  {
    consumedBytes = 0;
    int dataIndex = 0;

    if (!TryReadDeltaSize(data, length, ref dataIndex, out long sourceSize))
    {
      return false;
    }

    if (!TryReadDeltaSize(data, length, ref dataIndex, out long targetSize))
    {
      return false;
    }

    long bytesProduced = 0;

    while (dataIndex < length && bytesProduced < targetSize)
    {
      byte command = data[dataIndex++];

      if ((command & PackfileConstants.COPY_COMMAND_FLAG) != 0)
      {
        // COPY command: validate and track bytes produced
        if (!TryValidateCopyCommand(command, data, length, ref dataIndex, out int copySize))
        {
          return false;
        }
        bytesProduced += copySize;
      }
      else
      {
        // ADD command: validate and track bytes produced
        if (!TryValidateAddCommand(command, data, length, ref dataIndex, out int addSize))
        {
          return false;
        }
        bytesProduced += addSize;
      }
    }

    // Verify we produced exactly the target size
    if (bytesProduced != targetSize)
    {
      return false;
    }

    consumedBytes = dataIndex;
    return true;
  }

  private static bool TryValidateCopyCommand(byte command, byte[] data, int length, ref int index, out int copySize)
  {
    copySize = 0;
    int copyOffset = 0;

    // Read copy offset bytes
    if ((command & PackfileConstants.COPY_OFFSET_BYTE_0) != 0)
    {
      if (index >= length) return false;
      copyOffset |= data[index++];
    }
    if ((command & PackfileConstants.COPY_OFFSET_BYTE_1) != 0)
    {
      if (index >= length) return false;
      copyOffset |= data[index++] << 8;
    }
    if ((command & PackfileConstants.COPY_OFFSET_BYTE_2) != 0)
    {
      if (index >= length) return false;
      copyOffset |= data[index++] << 16;
    }
    if ((command & PackfileConstants.COPY_OFFSET_BYTE_3) != 0)
    {
      if (index >= length) return false;
      copyOffset |= data[index++] << 24;
    }

    // Read copy size bytes
    if ((command & PackfileConstants.COPY_SIZE_BYTE_0) != 0)
    {
      if (index >= length) return false;
      copySize |= data[index++];
    }
    if ((command & PackfileConstants.COPY_SIZE_BYTE_1) != 0)
    {
      if (index >= length) return false;
      copySize |= data[index++] << 8;
    }
    if ((command & PackfileConstants.COPY_SIZE_BYTE_2) != 0)
    {
      if (index >= length) return false;
      copySize |= data[index++] << 16;
    }

    // Size 0 means copy 64KB (Git's special case)
    if (copySize == 0)
    {
      copySize = PackfileConstants.DEFAULT_COPY_SIZE;
    }

    return true;
  }

  private static bool TryValidateAddCommand(byte command, byte[] data, int length, ref int index, out int addSize)
  {
    addSize = command & PackfileConstants.ADD_SIZE_MASK;

    if (index + addSize > length)
    {
      return false;
    }

    // Skip the add data (we're just validating, not reading it)
    index += addSize;
    return true;
  }

  private static bool TryReadDeltaSize(byte[] data, int length, ref int index, out long size)
  {
    size = 0;
    int bitShift = 0;

    while (index < length)
    {
      byte currentByte = data[index++];
      size |= (long)(currentByte & PackfileConstants.VAR_LEN_VALUE_MASK) << bitShift;

      // If bit 7 is not set, this is the last byte
      if ((currentByte & PackfileConstants.VAR_LEN_CONTINUATION_FLAG) == 0)
      {
        return true;
      }

      bitShift += 7;
    }

    return false;
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
