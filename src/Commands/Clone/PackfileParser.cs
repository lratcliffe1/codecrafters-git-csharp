using System.Text;
using codecrafters_git.src.Models;

namespace codecrafters_git.src.Commands.Clone;

public interface IPackfileParser
{
  int FindPackOffset(byte[] rawData);
  void ParseAllObjects(byte[] rawData, int packOffset);
}

public class PackfileParser(IObjectStore objectStore) : IPackfileParser
{
  public int FindPackOffset(byte[] rawData)
  {
    var pattern = Encoding.ASCII.GetBytes("PACK");

    for (int i = 0; i <= rawData.Length - pattern.Length; i++)
    {
      bool match = true;
      for (int j = 0; j < pattern.Length; j++)
      {
        if (rawData[i + j] != pattern[j]) { match = false; break; }
      }
      if (match) return i;
    }
    return -1;
  }

  public void ParseAllObjects(byte[] rawData, int packOffset)
  {
    // Track objects by their offset in the packfile for OffsetDelta resolution
    var objectsByOffset = new Dictionary<int, GitObject>();

    // Deltas that couldn't be resolved immediately (base object not available yet)
    var pendingRefDeltas = new List<PendingRefDelta>();
    var pendingOffsetDeltas = new List<PendingOffsetDelta>();

    // Read object count from packfile header (bytes 8-11)
    int objectCount = NetToHostInt32(rawData, packOffset + PackfileConstants.OBJECT_COUNT_OFFSET);
    int offset = packOffset + PackfileConstants.OBJECT_DATA_START_OFFSET;

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
        HandleRefDeltaObject(rawData, objectStartOffset, objectsByOffset, pendingRefDeltas, out bytesConsumed, ref offset);
      }
      else if (type == (int)GitType.OffsetDelta) // OffsetDelta: base object referenced by backward distance in packfile
      {
        HandleOffsetDeltaObject(rawData, objectStartOffset, objectsByOffset, pendingOffsetDeltas, out bytesConsumed, ref offset);
      }
      else // Standard object (Commit, Tree, Blob, Tag): full object data
      {
        objectData = PackfileInflater.InflateStandardObject(rawData, offset, uncompressedSize, out bytesConsumed);
        StoreResolvedObject(type, objectData, objectStartOffset, objectsByOffset);
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

  private void HandleRefDeltaObject(
    byte[] rawData,
    int objectStartOffset,
    Dictionary<int, GitObject> objectsByOffset,
    List<PendingRefDelta> pendingRefDeltas,
    out int bytesConsumed,
    ref int parserOffset)
  {
    string refBaseHash = ReadRefDeltaBaseHash(rawData, ref parserOffset);
    byte[] objectData = PackfileInflater.InflateDeltaObject(rawData, parserOffset, out bytesConsumed);

    TryResolveRefDelta(refBaseHash, objectData, objectStartOffset, objectsByOffset, pendingRefDeltas);
  }

  private void HandleOffsetDeltaObject(
    byte[] rawData,
    int objectStartOffset,
    Dictionary<int, GitObject> objectsByOffset,
    List<PendingOffsetDelta> pendingOffsetDeltas,
    out int bytesConsumed,
    ref int parserOffset)
  {
    int offsetAfterHeader = parserOffset;
    int backwardDistance = ReadOffsetDeltaDistance(rawData, ref parserOffset);
    int? baseOffset = objectStartOffset - backwardDistance;
    int? baseOffsetAlt = offsetAfterHeader - backwardDistance;
    byte[] objectData = PackfileInflater.InflateDeltaObject(rawData, parserOffset, out bytesConsumed);

    TryResolveOffsetDelta(baseOffset, baseOffsetAlt, objectData, objectStartOffset, objectsByOffset, pendingOffsetDeltas);
  }

  private void TryResolveRefDelta(
    string refBaseHash,
    byte[] deltaData,
    int objectStartOffset,
    Dictionary<int, GitObject> objectsByOffset,
    List<PendingRefDelta> pendingRefDeltas)
  {
    if (DeltaResolver.TryApplyRefDelta(objectStore, refBaseHash, deltaData, out int baseType, out byte[] resolved))
    {
      StoreResolvedObject(baseType, resolved, objectStartOffset, objectsByOffset);
    }
    else
    {
      // Base object not available yet: add to pending list for later resolution
      pendingRefDeltas.Add(new PendingRefDelta(refBaseHash, deltaData, objectStartOffset));
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
      StoreResolvedObject(baseType, resolved, objectStartOffset, objectsByOffset);
    }
    else if (baseOffset.HasValue)
    {
      // Base object not available yet: add to pending list for later resolution
      pendingOffsetDeltas.Add(new PendingOffsetDelta(baseOffset.Value, baseOffsetAlt, deltaData, objectStartOffset));
    }
  }


  private static int NetToHostInt32(byte[] data, int startIndex)
  {
    return (data[startIndex] << 24)
      | (data[startIndex + 1] << 16)
      | (data[startIndex + 2] << 8)
      | data[startIndex + 3];
  }

  private static string ReadRefDeltaBaseHash(byte[] data, ref int index)
  {
    if (index + PackfileConstants.SHA1_HASH_SIZE > data.Length)
    {
      throw new InvalidDataException("Truncated REF_DELTA base hash.");
    }

    byte[] hashBytes = data[index..(index + PackfileConstants.SHA1_HASH_SIZE)];
    index += PackfileConstants.SHA1_HASH_SIZE;
    return Convert.ToHexString(hashBytes).ToLowerInvariant();
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
      madeProgress |= TryResolvePendingRefDeltas(objectsByOffset, pendingRefDeltas);
      madeProgress |= TryResolvePendingOffsetDeltas(objectsByOffset, pendingOffsetDeltas);
    }
  }

  private bool TryResolvePendingRefDeltas(
    Dictionary<int, GitObject> objectsByOffset,
    List<PendingRefDelta> pendingRefDeltas)
  {
    return TryResolvePending(
      pendingRefDeltas,
      pending => DeltaResolver.TryApplyRefDelta(objectStore, pending.BaseHash, pending.DeltaData, out int baseType, out byte[] resolved)
        ? new ResolvedDelta(baseType, resolved, pending.ObjectOffset)
        : null,
      objectsByOffset);
  }

  private bool TryResolvePendingOffsetDeltas(
    Dictionary<int, GitObject> objectsByOffset,
    List<PendingOffsetDelta> pendingOffsetDeltas)
  {
    return TryResolvePending(
      pendingOffsetDeltas,
      pending => DeltaResolver.TryApplyOffsetDelta(objectsByOffset, pending.BaseOffset, pending.BaseOffsetAlt, pending.DeltaData, out int baseType, out byte[] resolved)
        ? new ResolvedDelta(baseType, resolved, pending.ObjectOffset)
        : null,
      objectsByOffset);
  }

  private bool TryResolvePending<TPending>(
    List<TPending> pendingList,
    Func<TPending, ResolvedDelta?> tryResolve,
    Dictionary<int, GitObject> objectsByOffset)
  {
    bool madeProgress = false;

    for (int i = pendingList.Count - 1; i >= 0; i--)
    {
      ResolvedDelta? resolved = tryResolve(pendingList[i]);
      if (!resolved.HasValue)
      {
        continue;
      }

      StoreResolvedObject(resolved.Value.Type, resolved.Value.Data, resolved.Value.Offset, objectsByOffset);
      RemoveBySwapWithLast(pendingList, i);
      madeProgress = true;
    }

    return madeProgress;
  }

  private void StoreResolvedObject(
    int objectType,
    byte[] objectData,
    int objectStartOffset,
    Dictionary<int, GitObject> objectsByOffset)
  {
    objectStore.StoreObject(objectType, objectData);
    objectsByOffset[objectStartOffset] = new GitObject(objectType, objectData);
  }

  private static void RemoveBySwapWithLast<T>(List<T> list, int index)
  {
    int lastIndex = list.Count - 1;
    if (index != lastIndex)
    {
      list[index] = list[lastIndex];
    }
    list.RemoveAt(lastIndex);
  }

  private readonly record struct ResolvedDelta(int Type, byte[] Data, int Offset);
}
