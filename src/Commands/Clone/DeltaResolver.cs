using codecrafters_git.src.Models;

namespace codecrafters_git.src.Commands.Clone;

public static class DeltaResolver
{
  public static bool TryApplyRefDelta(
    IObjectStore objectStore,
    string baseHash,
    byte[] deltaData,
    out int baseType,
    out byte[] resolved)
  {
    baseType = 0;
    resolved = [];

    if (!objectStore.TryGetObjectByHash(baseHash, out var baseObject))
    {
      return false;
    }

    if (!TryApplyDelta(baseObject.Data, deltaData, out byte[] output))
    {
      return false;
    }

    baseType = baseObject.Type;
    resolved = output;
    return true;
  }

  public static bool TryApplyOffsetDelta(
    Dictionary<int, GitObject> objectsByOffset,
    int? baseOffset,
    int? baseOffsetAlt,
    byte[] deltaData,
    out int baseType,
    out byte[] resolved)
  {
    baseType = 0;
    resolved = [];

    // Look up base object by its offset in the packfile
    if (!TryGetBaseObject(objectsByOffset, baseOffset, baseOffsetAlt, out var baseObject))
    {
      return false;
    }

    // Apply delta instructions to base object
    if (!TryApplyDelta(baseObject.Data, deltaData, out byte[] output))
    {
      return false;
    }

    baseType = baseObject.Type;
    resolved = output;
    return true;
  }

  private static bool TryApplyDelta(byte[] baseData, byte[] deltaData, out byte[] output)
  {
    output = [];
    int deltaIndex = 0;

    // Read source and target sizes (variable-length encoded)
    long sourceSize = ReadDeltaSize(deltaData, ref deltaIndex);
    long targetSize = ReadDeltaSize(deltaData, ref deltaIndex);

    if (targetSize > int.MaxValue)
    {
      throw new InvalidOperationException("Delta target too large to materialize.");
    }

    byte[] result = new byte[targetSize];
    int resultPosition = 0;

    // Process delta commands until we've produced the target size
    while (deltaIndex < deltaData.Length && resultPosition < targetSize)
    {
      byte command = deltaData[deltaIndex++];

      if ((command & PackfileConstants.COPY_COMMAND_FLAG) != 0)
      {
        // COPY command: copy data from base object
        if (!TryExecuteCopyCommand(command, deltaData, ref deltaIndex, baseData, result, ref resultPosition))
        {
          return false;
        }
      }
      else
      {
        // ADD command: add new data from delta
        if (!TryExecuteAddCommand(command, deltaData, ref deltaIndex, result, ref resultPosition))
        {
          return false;
        }
      }
    }

    // Verify we produced exactly the target size
    if (resultPosition != targetSize)
    {
      return false;
    }

    output = result;
    return true;
  }

  private static bool TryExecuteCopyCommand(
    byte command,
    byte[] deltaData,
    ref int deltaIndex,
    byte[] baseData,
    byte[] result,
    ref int resultPosition)
  {
    int copyOffset = 0;
    int copySize = 0;

    // Read copy offset bytes (variable-length, up to 4 bytes)
    if ((command & PackfileConstants.COPY_OFFSET_BYTE_0) != 0) copyOffset |= deltaData[deltaIndex++];
    if ((command & PackfileConstants.COPY_OFFSET_BYTE_1) != 0) copyOffset |= deltaData[deltaIndex++] << 8;
    if ((command & PackfileConstants.COPY_OFFSET_BYTE_2) != 0) copyOffset |= deltaData[deltaIndex++] << 16;
    if ((command & PackfileConstants.COPY_OFFSET_BYTE_3) != 0) copyOffset |= deltaData[deltaIndex++] << 24;

    // Read copy size bytes (variable-length, up to 3 bytes)
    if ((command & PackfileConstants.COPY_SIZE_BYTE_0) != 0) copySize |= deltaData[deltaIndex++];
    if ((command & PackfileConstants.COPY_SIZE_BYTE_1) != 0) copySize |= deltaData[deltaIndex++] << 8;
    if ((command & PackfileConstants.COPY_SIZE_BYTE_2) != 0) copySize |= deltaData[deltaIndex++] << 16;

    // Size 0 means copy 64KB (Git's special case)
    if (copySize == 0)
    {
      copySize = PackfileConstants.DEFAULT_COPY_SIZE;
    }

    // Validate bounds
    if (copyOffset < 0 || copyOffset + copySize > baseData.Length)
    {
      return false;
    }

    // Copy data from base to result
    Buffer.BlockCopy(baseData, copyOffset, result, resultPosition, copySize);
    resultPosition += copySize;
    return true;
  }

  private static bool TryExecuteAddCommand(
    byte command,
    byte[] deltaData,
    ref int deltaIndex,
    byte[] result,
    ref int resultPosition)
  {
    // ADD command: lower 7 bits contain the size
    int addSize = command & PackfileConstants.ADD_SIZE_MASK;

    if (addSize > 0)
    {
      // Validate bounds
      if (deltaIndex + addSize > deltaData.Length || resultPosition + addSize > result.Length)
      {
        return false;
      }

      // Copy new data from delta to result
      Buffer.BlockCopy(deltaData, deltaIndex, result, resultPosition, addSize);
      deltaIndex += addSize;
      resultPosition += addSize;
    }

    return true;
  }

  private static long ReadDeltaSize(byte[] data, ref int index)
  {
    long size = 0;
    int bitShift = 0;

    while (index < data.Length)
    {
      byte currentByte = data[index++];
      size |= (long)(currentByte & PackfileConstants.VAR_LEN_VALUE_MASK) << bitShift;

      // If bit 7 is not set, this is the last byte
      if ((currentByte & PackfileConstants.VAR_LEN_CONTINUATION_FLAG) == 0)
      {
        break;
      }

      bitShift += 7;
    }

    return size;
  }

  private static bool TryGetBaseObject(
    Dictionary<int, GitObject> objectsByOffset,
    int? baseOffset,
    int? baseOffsetAlt,
    out GitObject baseObject)
  {
    // Try primary offset first (calculated from object start)
    if (baseOffset.HasValue && objectsByOffset.TryGetValue(baseOffset.Value, out baseObject))
    {
      return true;
    }

    // Try alternative offset (calculated from after header)
    if (baseOffsetAlt.HasValue && objectsByOffset.TryGetValue(baseOffsetAlt.Value, out baseObject))
    {
      return true;
    }

    baseObject = default;
    return false;
  }
}
