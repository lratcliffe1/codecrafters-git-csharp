namespace Commands;

internal sealed class DeltaResolver
{
  public static bool TryApplyRefDelta(
    ObjectStore objectStore,
    string baseHash,
    byte[] deltaData,
    out int baseType,
    out byte[] resolved)
  {
    baseType = 0;
    resolved = [];

    if (!objectStore.TryGetObjectByHash(baseHash, out var baseObject))
    {
      CloneLogger.Log($"RefDelta base not found: {baseHash}");
      return false;
    }

    if (!TryApplyDelta(baseObject.Data, deltaData, out byte[] output))
    {
      CloneLogger.Log($"RefDelta apply failed for base {baseHash}");
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
    resolved = Array.Empty<byte>();

    if (!TryGetBaseObject(objectsByOffset, baseOffset, baseOffsetAlt, out var baseObject))
    {
      CloneLogger.Log($"OFSDelta base not found: {baseOffset} or {baseOffsetAlt}");
      return false;
    }

    if (!TryApplyDelta(baseObject.Data, deltaData, out byte[] output))
    {
      CloneLogger.Log($"OFSDelta apply failed for base {baseOffset} or {baseOffsetAlt}");
      return false;
    }

    baseType = baseObject.Type;
    resolved = output;
    return true;
  }

  private static bool TryApplyDelta(byte[] baseData, byte[] deltaData, out byte[] output)
  {
    output = [];
    int index = 0;

    long sourceSize = ReadDeltaSize(deltaData, ref index);
    long targetSize = ReadDeltaSize(deltaData, ref index);

    if (sourceSize != baseData.Length)
    {
      CloneLogger.Log("Delta source size mismatch; continuing anyway.");
    }

    if (targetSize > int.MaxValue)
    {
      throw new InvalidOperationException("Delta target too large to materialize.");
    }

    byte[] result = new byte[targetSize];
    int outPos = 0;

    while (index < deltaData.Length && outPos < targetSize)
    {
      byte cmd = deltaData[index++];
      if ((cmd & 0x80) != 0)
      {
        int copyOffset = 0;
        int copySize = 0;

        if ((cmd & 0x01) != 0) copyOffset |= deltaData[index++];
        if ((cmd & 0x02) != 0) copyOffset |= deltaData[index++] << 8;
        if ((cmd & 0x04) != 0) copyOffset |= deltaData[index++] << 16;
        if ((cmd & 0x08) != 0) copyOffset |= deltaData[index++] << 24;

        if ((cmd & 0x10) != 0) copySize |= deltaData[index++];
        if ((cmd & 0x20) != 0) copySize |= deltaData[index++] << 8;
        if ((cmd & 0x40) != 0) copySize |= deltaData[index++] << 16;

        if (copySize == 0)
        {
          copySize = 0x10000;
        }

        if (copyOffset < 0 || copyOffset + copySize > baseData.Length)
        {
          return false;
        }

        Buffer.BlockCopy(baseData, copyOffset, result, outPos, copySize);
        outPos += copySize;
      }
      else
      {
        int addSize = cmd & 0x7F;
        if (addSize > 0)
        {
          if (index + addSize > deltaData.Length || outPos + addSize > result.Length)
          {
            return false;
          }

          Buffer.BlockCopy(deltaData, index, result, outPos, addSize);
          index += addSize;
          outPos += addSize;
        }
      }
    }

    if (outPos != targetSize)
    {
      return false;
    }

    output = result;
    return true;
  }

  private static long ReadDeltaSize(byte[] data, ref int index)
  {
    long size = 0;
    int shift = 0;
    while (index < data.Length)
    {
      byte b = data[index++];
      size |= (long)(b & 0x7F) << shift;
      if ((b & 0x80) == 0)
      {
        break;
      }
      shift += 7;
    }
    return size;
  }

  private static bool TryGetBaseObject(
    Dictionary<int, GitObject> objectsByOffset,
    int? baseOffset,
    int? baseOffsetAlt,
    out GitObject baseObject)
  {
    if (baseOffset.HasValue && objectsByOffset.TryGetValue(baseOffset.Value, out baseObject))
    {
      return true;
    }

    if (baseOffsetAlt.HasValue && objectsByOffset.TryGetValue(baseOffsetAlt.Value, out baseObject))
    {
      return true;
    }

    baseObject = default;
    return false;
  }
}
