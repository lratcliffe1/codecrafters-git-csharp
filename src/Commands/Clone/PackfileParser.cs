namespace Commands;

internal sealed class PackfileParser(ObjectStore objectStore)
{
  public void ParseAllObjects(byte[] rawData)
  {
    var objectsByOffset = new Dictionary<int, GitObject>();
    var pendingRefDeltas = new List<PendingRefDelta>();
    var pendingOffsetDeltas = new List<PendingOffsetDelta>();

    int objectCount = NetToHostInt32(rawData, 8);
    int offset = 12;

    for (int i = 0; i < objectCount; i++)
    {
      if (offset >= rawData.Length - 20)
      {
        break;
      }

      int objectStartOffset = offset;
      int shift = 4;
      byte b = rawData[offset++];

      int type = (b >> 4) & 7;
      long uncompressedSize = b & 15;

      while ((b & 0x80) != 0)
      {
        b = rawData[offset++];
        uncompressedSize |= (long)(b & 0x7F) << shift;
        shift += 7;
      }

      byte[] objectData;
      int bytesConsumed;

      string? refBaseHash = null;
      int? ofsBaseOffset = null;
      int? ofsBaseOffsetAlt = null;

      if (type == (int)GitType.RefDelta)
      {
        refBaseHash = ReadRefDeltaBaseHash(rawData, ref offset);
        objectData = PackfileInflater.InflateDeltaObject(rawData, offset, out bytesConsumed);
      }
      else if (type == (int)GitType.OffsetDelta)
      {
        int offsetAfterHeader = offset;
        int distance = ReadOffsetDeltaDistance(rawData, ref offset);
        ofsBaseOffset = objectStartOffset - distance;
        ofsBaseOffsetAlt = offsetAfterHeader - distance;
        objectData = PackfileInflater.InflateDeltaObject(rawData, offset, out bytesConsumed);
      }
      else
      {
        objectData = PackfileInflater.InflateStandardObject(rawData, offset, uncompressedSize, out bytesConsumed);
      }

      if (type <= 4)
      {
        objectStore.StoreObject(type, objectData);
        objectsByOffset[objectStartOffset] = new GitObject(type, objectData);
      }
      else if (type == (int)GitType.RefDelta)
      {
        if (refBaseHash != null &&
            DeltaResolver.TryApplyRefDelta(objectStore, refBaseHash, objectData, out int baseType, out byte[] resolved))
        {
          objectStore.StoreObject(baseType, resolved);
          objectsByOffset[objectStartOffset] = new GitObject(baseType, resolved);
        }
        else
        {
          pendingRefDeltas.Add(new PendingRefDelta(refBaseHash ?? string.Empty, objectData, objectStartOffset));
        }
      }
      else if (type == (int)GitType.OffsetDelta)
      {
        if (DeltaResolver.TryApplyOffsetDelta(objectsByOffset, ofsBaseOffset, ofsBaseOffsetAlt, objectData, out int baseType, out byte[] resolved))
        {
          objectStore.StoreObject(baseType, resolved);
          objectsByOffset[objectStartOffset] = new GitObject(baseType, resolved);
        }
        else
        {
          if (ofsBaseOffset.HasValue)
          {
            pendingOffsetDeltas.Add(new PendingOffsetDelta(ofsBaseOffset.Value, ofsBaseOffsetAlt, objectData, objectStartOffset));
          }
          else
          {
            CloneLogger.Log("OffsetDelta base not found; skipping.");
          }
        }
      }
      else
      {
        CloneLogger.Log("Object is a Delta (needs base object to read).");
      }

      offset += bytesConsumed;
    }

    ResolvePendingDeltas(objectsByOffset, pendingRefDeltas, pendingOffsetDeltas);
  }

  private static int NetToHostInt32(byte[] data, int startIndex)
  {
    var bytes = data.Skip(startIndex).Take(4).Reverse().ToArray();
    return BitConverter.ToInt32(bytes, 0);
  }

  private static string ReadRefDeltaBaseHash(byte[] data, ref int index)
  {
    if (index + 20 > data.Length)
    {
      throw new InvalidDataException("Truncated REF_DELTA base hash.");
    }

    byte[] hashBytes = data[index..(index + 20)];
    index += 20;
    return Convert.ToHexString(hashBytes).ToLower();
  }

  private static int ReadOffsetDeltaDistance(byte[] data, ref int index)
  {
    byte b = data[index++];
    int value = b & 0x7F;
    while ((b & 0x80) != 0 && index < data.Length)
    {
      b = data[index++];
      value = ((value + 1) << 7) | (b & 0x7F);
    }

    return value;
  }

  private void ResolvePendingDeltas(
    Dictionary<int, GitObject> objectsByOffset,
    List<PendingRefDelta> pendingRefDeltas,
    List<PendingOffsetDelta> pendingOffsetDeltas)
  {
    bool progress = true;
    while (progress && (pendingRefDeltas.Count > 0 || pendingOffsetDeltas.Count > 0))
    {
      progress = false;
      for (int i = pendingRefDeltas.Count - 1; i >= 0; i--)
      {
        PendingRefDelta pending = pendingRefDeltas[i];
        if (DeltaResolver.TryApplyRefDelta(objectStore, pending.BaseHash, pending.DeltaData, out int baseType, out byte[] resolved))
        {
          objectStore.StoreObject(baseType, resolved);
          objectsByOffset[pending.ObjectOffset] = new GitObject(baseType, resolved);
          pendingRefDeltas.RemoveAt(i);
          progress = true;
        }
      }

      for (int i = pendingOffsetDeltas.Count - 1; i >= 0; i--)
      {
        PendingOffsetDelta pending = pendingOffsetDeltas[i];
        if (DeltaResolver.TryApplyOffsetDelta(objectsByOffset, pending.BaseOffset, pending.BaseOffsetAlt, pending.DeltaData, out int baseType, out byte[] resolved))
        {
          objectStore.StoreObject(baseType, resolved);
          objectsByOffset[pending.ObjectOffset] = new GitObject(baseType, resolved);
          pendingOffsetDeltas.RemoveAt(i);
          progress = true;
        }
      }
    }
  }
}
