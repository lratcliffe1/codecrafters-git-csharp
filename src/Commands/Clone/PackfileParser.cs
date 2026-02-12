using System.Text;
using codecrafters_git.src.Models;

namespace codecrafters_git.src.Commands.Clone;

public interface IPackfileParser
{
  byte[] RemovePackHeader(byte[] rawData);
  void ParseAllObjects(byte[] rawData);
}

public class PackfileParser(IObjectStore objectStore) : IPackfileParser
{
  public byte[] RemovePackHeader(byte[] rawData)
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

  public void ParseAllObjects(byte[] rawData)
  {
    // Track objects by their offset in the packfile for OffsetDelta resolution
    var objectsByOffset = new Dictionary<int, GitObject>();

    // Deltas that couldn't be resolved immediately (base object not available yet)
    var pendingRefDeltas = new List<PendingRefDelta>();
    var pendingOffsetDeltas = new List<PendingOffsetDelta>();

    // Read object count from packfile header (bytes 8-11)
    int objectCount = NetToHostInt32(rawData, PackfileConstants.OBJECT_COUNT_OFFSET);
    int offset = PackfileConstants.OBJECT_DATA_START_OFFSET;

    for (int i = 0; i < objectCount; i++)
    {
      // Stop if we've reached the checksum area at the end
      if (offset >= rawData.Length - PackfileConstants.PACKFILE_CHECKSUM_SIZE)
      {
        break;
      }

      // Remember where this object starts for OffsetDelta references
      int objectStartOffset = offset;

      // Parse object header: type and size are variable-length encoded
      (int type, long uncompressedSize) = ParseObjectHeader(rawData, ref offset);

      byte[] objectData;
      int bytesConsumed;

      if (type == (int)GitType.RefDelta) // RefDelta: base object referenced by SHA-1 hash
      {
        string? refBaseHash = ReadRefDeltaBaseHash(rawData, ref offset);
        objectData = PackfileInflater.InflateDeltaObject(rawData, offset, out bytesConsumed);

        TryResolveRefDelta(refBaseHash, objectData, objectStartOffset, objectsByOffset, pendingRefDeltas);
      }
      else if (type == (int)GitType.OffsetDelta) // OffsetDelta: base object referenced by backward distance in packfile
      {
        int offsetAfterHeader = offset;
        int backwardDistance = ReadOffsetDeltaDistance(rawData, ref offset);
        int? baseOffset = objectStartOffset - backwardDistance;
        int? baseOffsetAlt = offsetAfterHeader - backwardDistance;
        objectData = PackfileInflater.InflateDeltaObject(rawData, offset, out bytesConsumed);

        TryResolveOffsetDelta(baseOffset, baseOffsetAlt, objectData, objectStartOffset, objectsByOffset, pendingOffsetDeltas);
      }
      else // Standard object (Commit, Tree, Blob, Tag): full object data
      {
        objectData = PackfileInflater.InflateStandardObject(rawData, offset, uncompressedSize, out bytesConsumed);

        StoreStandardObject(type, objectData, objectStartOffset, objectsByOffset);
      }

      offset += bytesConsumed;
    }

    // Resolve any deltas that couldn't be resolved during first pass
    ResolvePendingDeltas(objectsByOffset, pendingRefDeltas, pendingOffsetDeltas);
  }

  private static (int type, long uncompressedSize) ParseObjectHeader(byte[] rawData, ref int offset)
  {
    int shift = PackfileConstants.OBJECT_SIZE_SHIFT_START;
    byte headerByte = rawData[offset++];

    // Extract type from bits 4-6
    int type = (headerByte >> PackfileConstants.OBJECT_TYPE_SHIFT) & 7;

    // Extract initial size from lower 4 bits
    long uncompressedSize = headerByte & PackfileConstants.OBJECT_SIZE_LOW_BITS;

    // Continue reading size bytes if bit 7 is set (variable-length encoding)
    while ((headerByte & PackfileConstants.OBJECT_SIZE_CONTINUATION_FLAG) != 0)
    {
      headerByte = rawData[offset++];
      uncompressedSize |= (long)(headerByte & PackfileConstants.OBJECT_SIZE_MASK) << shift;
      shift += PackfileConstants.OBJECT_SIZE_SHIFT_INCREMENT;
    }

    return (type, uncompressedSize);
  }

  private void StoreStandardObject(
    int type,
    byte[] objectData,
    int objectStartOffset,
    Dictionary<int, GitObject> objectsByOffset)
  {
    objectStore.StoreObject(type, objectData);
    objectsByOffset[objectStartOffset] = new GitObject(type, objectData);
  }

  private void TryResolveRefDelta(
    string? refBaseHash,
    byte[] deltaData,
    int objectStartOffset,
    Dictionary<int, GitObject> objectsByOffset,
    List<PendingRefDelta> pendingRefDeltas)
  {
    if (refBaseHash != null && DeltaResolver.TryApplyRefDelta(objectStore, refBaseHash, deltaData, out int baseType, out byte[] resolved))
    {
      // Successfully resolved: store the resolved object
      objectStore.StoreObject(baseType, resolved);
      objectsByOffset[objectStartOffset] = new GitObject(baseType, resolved);
    }
    else
    {
      // Base object not available yet: add to pending list for later resolution
      pendingRefDeltas.Add(new PendingRefDelta(refBaseHash ?? string.Empty, deltaData, objectStartOffset));
    }
  }

  private void TryResolveOffsetDelta(
    int? baseOffset,
    int? baseOffsetAlt,
    byte[] deltaData,
    int objectStartOffset,
    Dictionary<int, GitObject> objectsByOffset,
    List<PendingOffsetDelta> pendingOffsetDeltas)
  {
    if (DeltaResolver.TryApplyOffsetDelta(objectsByOffset, baseOffset, baseOffsetAlt, deltaData, out int baseType, out byte[] resolved))
    {
      // Successfully resolved: store the resolved object
      objectStore.StoreObject(baseType, resolved);
      objectsByOffset[objectStartOffset] = new GitObject(baseType, resolved);
    }
    else if (baseOffset.HasValue)
    {
      // Base object not available yet: add to pending list for later resolution
      pendingOffsetDeltas.Add(new PendingOffsetDelta(baseOffset.Value, baseOffsetAlt, deltaData, objectStartOffset));
    }
  }


  private static int NetToHostInt32(byte[] data, int startIndex)
  {
    var bytes = data.Skip(startIndex).Take(4).Reverse().ToArray();
    return BitConverter.ToInt32(bytes, 0);
  }

  private static string ReadRefDeltaBaseHash(byte[] data, ref int index)
  {
    if (index + PackfileConstants.SHA1_HASH_SIZE > data.Length)
    {
      throw new InvalidDataException("Truncated REF_DELTA base hash.");
    }

    byte[] hashBytes = data[index..(index + PackfileConstants.SHA1_HASH_SIZE)];
    index += PackfileConstants.SHA1_HASH_SIZE;
    return Convert.ToHexString(hashBytes).ToLower();
  }

  private static int ReadOffsetDeltaDistance(byte[] data, ref int index)
  {
    byte currentByte = data[index++];
    int distance = currentByte & PackfileConstants.VAR_LEN_VALUE_MASK;

    // Continue reading if bit 7 is set (more bytes follow)
    while ((currentByte & PackfileConstants.VAR_LEN_CONTINUATION_FLAG) != 0 && index < data.Length)
    {
      currentByte = data[index++];
      // Git's encoding: (value + 1) << 7 | next_byte
      distance = ((distance + 1) << PackfileConstants.OBJECT_SIZE_SHIFT_INCREMENT) | (currentByte & PackfileConstants.VAR_LEN_VALUE_MASK);
    }

    return distance;
  }

  private void ResolvePendingDeltas(
    Dictionary<int, GitObject> objectsByOffset,
    List<PendingRefDelta> pendingRefDeltas,
    List<PendingOffsetDelta> pendingOffsetDeltas)
  {
    bool madeProgress = true;

    while (madeProgress && (pendingRefDeltas.Count > 0 || pendingOffsetDeltas.Count > 0))
    {
      madeProgress = false;

      // Try to resolve pending RefDeltas (iterate backwards to safely remove items)
      for (int i = pendingRefDeltas.Count - 1; i >= 0; i--)
      {
        PendingRefDelta pending = pendingRefDeltas[i];
        if (DeltaResolver.TryApplyRefDelta(objectStore, pending.BaseHash, pending.DeltaData, out int baseType, out byte[] resolved))
        {
          objectStore.StoreObject(baseType, resolved);
          objectsByOffset[pending.ObjectOffset] = new GitObject(baseType, resolved);
          pendingRefDeltas.RemoveAt(i);
          madeProgress = true;
        }
      }

      // Try to resolve pending OffsetDeltas (iterate backwards to safely remove items)
      for (int i = pendingOffsetDeltas.Count - 1; i >= 0; i--)
      {
        PendingOffsetDelta pending = pendingOffsetDeltas[i];
        if (DeltaResolver.TryApplyOffsetDelta(objectsByOffset, pending.BaseOffset, pending.BaseOffsetAlt, pending.DeltaData, out int baseType, out byte[] resolved))
        {
          objectStore.StoreObject(baseType, resolved);
          objectsByOffset[pending.ObjectOffset] = new GitObject(baseType, resolved);
          pendingOffsetDeltas.RemoveAt(i);
          madeProgress = true;
        }
      }
    }
  }
}
