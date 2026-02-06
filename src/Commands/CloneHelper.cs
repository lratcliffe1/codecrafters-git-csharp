using System.IO.Compression;
using System.Text;
using Helpers;

namespace Commands;

public class CloneHelper()
{
  private static readonly HttpClient client = new HttpClient();
  private static bool DebugLog = false;

  public static async Task<string> Clone(string repositoryUrl, string targetDirectory)
  {
    CreateDirectory(targetDirectory);

    (string sha1, string headRef) = await DiscoverReferences(repositoryUrl);

    byte[] packFile = await FetchPackfile(repositoryUrl, sha1);
    byte[] packData = ExtractPackData(packFile);

    string originalCwd = Directory.GetCurrentDirectory();
    string workTreeRoot = Path.Combine(originalCwd, targetDirectory);
    Directory.SetCurrentDirectory(targetDirectory);
    try
    {
      var objectsByHash = ParseAllObjects(packData);
      WriteHeadAndRef(headRef, sha1);
      CheckoutWorkingTree(objectsByHash, workTreeRoot, sha1);
    }
    finally
    {
      Directory.SetCurrentDirectory(originalCwd);
    }

    return "";
  }

  static void CreateDirectory(string targetDirectory)
  {
    InitHelper.Init(targetDirectory + "/");
  }

  static async Task<(string Sha, string HeadRef)> DiscoverReferences(string repositoryUrl)
  {
    string url = $"{repositoryUrl.TrimEnd('/')}/info/refs?service=git-upload-pack";

    try
    {
      var request = new HttpRequestMessage(HttpMethod.Get, url);
      request.Headers.Add("User-Agent", "git/2.0.0");
      request.Headers.Add("Accept", "*/*");

      HttpResponseMessage response = await client.SendAsync(request);
      response.EnsureSuccessStatusCode();

      string content = await response.Content.ReadAsStringAsync();

      var lines = content.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
      string? headSha = null;
      string? headRef = null;

      foreach (var rawLine in lines)
      {
        string line = StripPktLinePrefix(rawLine);
        if (line.Contains("symref=HEAD:refs/heads/"))
        {
          int start = line.IndexOf("symref=HEAD:", StringComparison.Ordinal);
          if (start >= 0)
          {
            string symref = line[(start + "symref=HEAD:".Length)..];
            int nullIndex = symref.IndexOf('\0');
            headRef = nullIndex >= 0 ? symref[..nullIndex] : symref;
          }
        }

        int headIndex = line.IndexOf(" HEAD", StringComparison.Ordinal);
        if (headIndex >= 40)
        {
          headSha = line.Substring(headIndex - 40, 40);
        }
      }

      if (headSha != null)
      {
        return (headSha, headRef ?? "refs/heads/master");
      }
    }
    catch (Exception e)
    {
      Console.WriteLine($"Error retrieving SHA-1: {e.Message}");
    }

    return ("", "refs/heads/master");
  }

  static async Task<byte[]> FetchPackfile(string repositoryUrl, string sha1)
  {
    string url = $"{repositoryUrl.TrimEnd('/')}/git-upload-pack";

    string body = $"0032want {sha1}\n00000009done\n";

    var request = new HttpRequestMessage(HttpMethod.Post, url)
    {
      Content = new StringContent(body)
    };

    request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-git-upload-pack-request");

    HttpResponseMessage response = await client.SendAsync(request);

    return await response.Content.ReadAsByteArrayAsync();
  }

  static byte[] ExtractPackData(byte[] rawData)
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
          Log($"Deframe: invalid pkt length at index={index}");
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
          Log($"Deframe: payloadLength={payloadLength} index={index} rawLen={rawData.Length}");
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
      Log($"Deframe: rawLen={rawData.Length}, deframedLen={deframed.Length}");
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


  static int ReadPktLineLength(byte[] data, int index)
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

  static int HexValue(byte c)
  {
    if (c >= (byte)'0' && c <= (byte)'9') return c - (byte)'0';
    if (c >= (byte)'a' && c <= (byte)'f') return c - (byte)'a' + 10;
    if (c >= (byte)'A' && c <= (byte)'F') return c - (byte)'A' + 10;
    return -1;
  }

  static bool StartsWithPack(byte[] data, int offset, int length)
  {
    return length >= 4 &&
           data[offset] == (byte)'P' &&
           data[offset + 1] == (byte)'A' &&
           data[offset + 2] == (byte)'C' &&
           data[offset + 3] == (byte)'K';
  }

  static Dictionary<string, GitObject> ParseAllObjects(byte[] rawData)
  {
    var objectsByHash = new Dictionary<string, GitObject>();
    var objectsByOffset = new Dictionary<int, GitObject>();
    var pendingRefDeltas = new List<PendingRefDelta>();
    var pendingOffsetDeltas = new List<PendingOffsetDelta>();

    int offset = FindPattern(rawData, Encoding.ASCII.GetBytes("PACK"));
    if (offset == -1) throw new Exception("Not a valid Packfile.");

    int objectCount = NetToHostInt32(rawData, offset + 8);

    offset += 12;

    for (int i = 0; i < objectCount; i++)
    {
      if (offset >= rawData.Length - 20)
      {
        break;
      }

      int objectStartOffset = offset;

      long uncompressedSize = 0;
      int shift = 4;
      byte b = rawData[offset++];

      int type = (b >> 4) & 7;
      uncompressedSize = b & 15;

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
        objectData = InflateDeltaObject(rawData, offset, out bytesConsumed);
      }
      else if (type == (int)GitType.OffsetDelta)
      {
        int offsetAfterHeader = offset;
        int distance = ReadOffsetDeltaDistance(rawData, ref offset);
        ofsBaseOffset = objectStartOffset - distance;
        ofsBaseOffsetAlt = offsetAfterHeader - distance;
        objectData = InflateDeltaObject(rawData, offset, out bytesConsumed);
      }
      else
      {
        objectData = InflateStandardObject(rawData, offset, uncompressedSize, out bytesConsumed);
      }

      if (type <= 4)
      {
        StoreObject(objectsByHash, type, objectData);
        objectsByOffset[objectStartOffset] = new GitObject(type, objectData);
      }
      else if (type == (int)GitType.RefDelta)
      {
        if (refBaseHash != null &&
            TryApplyRefDelta(objectsByHash, refBaseHash, objectData, out int baseType, out byte[] resolved))
        {
          StoreObject(objectsByHash, baseType, resolved);
          objectsByOffset[objectStartOffset] = new GitObject(baseType, resolved);
        }
        else
        {
          pendingRefDeltas.Add(new PendingRefDelta(refBaseHash ?? string.Empty, objectData, objectStartOffset));
        }
      }
      else if (type == (int)GitType.OffsetDelta)
      {
        if (TryApplyOffsetDelta(objectsByOffset, ofsBaseOffset, ofsBaseOffsetAlt, objectData, out int baseType, out byte[] resolved))
        {
          StoreObject(objectsByHash, baseType, resolved);
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
            Log("OffsetDelta base not found; skipping.");
          }
        }
      }
      else
      {
        Log("Object is a Delta (needs base object to read).");
      }

      offset += bytesConsumed;
    }

    ResolvePendingDeltas(objectsByHash, objectsByOffset, pendingRefDeltas, pendingOffsetDeltas);
    return objectsByHash;
  }

  static int NetToHostInt32(byte[] data, int startIndex)
  {
    var bytes = data.Skip(startIndex).Take(4).Reverse().ToArray();
    return BitConverter.ToInt32(bytes, 0);
  }

  static int FindPattern(byte[] data, byte[] pattern)
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

  static void WriteHeadAndRef(string headRef, string sha)
  {
    File.WriteAllText(".git/HEAD", $"ref: {headRef}\n");
    string refPath = Path.Combine(".git", headRef.Replace('/', Path.DirectorySeparatorChar));
    string? refDir = Path.GetDirectoryName(refPath);
    if (refDir != null && !Directory.Exists(refDir))
    {
      Directory.CreateDirectory(refDir);
    }
    File.WriteAllText(refPath, $"{sha}\n");
  }

  static void CheckoutWorkingTree(Dictionary<string, GitObject> objectsByHash, string targetDirectory, string commitHash)
  {
    if (!TryGetObjectByHash(objectsByHash, commitHash, out var commitObj))
    {
      Log("Commit object not found; skipping checkout.");
      return;
    }

    Log($"Checkout: commit {commitHash} found.");

    string commitText = Encoding.UTF8.GetString(commitObj.Data);
    string? treeHash = commitText
      .Split('\n')
      .FirstOrDefault(line => line.StartsWith("tree ", StringComparison.Ordinal))?
      .Split(' ', 2)
      .Last();

    if (string.IsNullOrWhiteSpace(treeHash))
    {
      Log("Tree hash not found in commit; skipping checkout.");
      return;
    }

    Log($"Checkout: root tree {treeHash.Trim()}");

    CheckoutTree(objectsByHash, treeHash.Trim(), targetDirectory);
  }

  static void CheckoutTree(Dictionary<string, GitObject> objectsByHash, string treeHash, string targetDirectory)
  {
    if (!TryGetObjectByHash(objectsByHash, treeHash, out var treeObj))
    {
      Log($"Tree lookup failed for {treeHash}. Stored? {objectsByHash.ContainsKey(treeHash)}");
      Log($"Tree object {treeHash} not found; skipping.");
      return;
    }

    Log($"CheckoutTree: {treeHash} -> {targetDirectory}");

    // Tree entries are: "<mode> <name>\\0<20-byte-sha>" repeated.
    byte[] data = treeObj.Data;
    // Cursor to walk the binary tree format.
    int i = 0;
    while (i < data.Length)
    {
      // Find the end of the mode string.
      int spaceIndex = Array.IndexOf(data, (byte)' ', i);
      string mode = Encoding.UTF8.GetString(data, i, spaceIndex - i);

      // Find the end of the filename.
      int nullIndex = Array.IndexOf(data, (byte)0, spaceIndex + 1);
      string name = Encoding.UTF8.GetString(data, spaceIndex + 1, nullIndex - (spaceIndex + 1));

      // Read the 20-byte object id right after the null.
      byte[] hashBytes = data[(nullIndex + 1)..(nullIndex + 21)];
      string hexHash = Convert.ToHexString(hashBytes).ToLower();

      // Build the full filesystem path for this entry.
      string path = Path.Combine(targetDirectory, name);
      if (mode.StartsWith("40000", StringComparison.Ordinal))
      {
        // Mode 40000 is a sub-tree (directory).
        Directory.CreateDirectory(path);
        Log($"Dir: {path} (tree {hexHash})");
        // Recurse into the subtree.
        CheckoutTree(objectsByHash, hexHash, path);
      }
      else
      {
        // File blob: write raw blob contents to disk.
        if (TryGetObjectByHash(objectsByHash, hexHash, out var blobObj))
        {
          // Ensure parent directory exists before writing file.
          Directory.CreateDirectory(Path.GetDirectoryName(path) ?? targetDirectory);
          File.WriteAllBytes(path, blobObj.Data);
          Log($"File: {path} (blob {hexHash}, {blobObj.Data.Length} bytes)");
        }
        else
        {
          Log($"Missing blob {hexHash} for path {path}");
        }
      }

      // Move to the next entry: null terminator + 20 bytes of hash.
      i = nullIndex + 21;
    }
  }

  static void ResolvePendingDeltas(
    Dictionary<string, GitObject> objectsByHash,
    Dictionary<int, GitObject> objectsByOffset,
    List<PendingRefDelta> pendingRefDeltas,
    List<PendingOffsetDelta> pendingOffsetDeltas)
  {
    // Keep looping while we can resolve new deltas.
    bool progress = true;
    while (progress && (pendingRefDeltas.Count > 0 || pendingOffsetDeltas.Count > 0))
    {
      // Reset progress at the start of each pass.
      progress = false;
      for (int i = pendingRefDeltas.Count - 1; i >= 0; i--)
      {
        PendingRefDelta pending = pendingRefDeltas[i];
        // Try to resolve using the base hash.
        if (TryApplyRefDelta(objectsByHash, pending.BaseHash, pending.DeltaData, out int baseType, out byte[] resolved))
        {
          StoreObject(objectsByHash, baseType, resolved);
          // Store by offset so OFS_DELTA can reference this later.
          objectsByOffset[pending.ObjectOffset] = new GitObject(baseType, resolved);
          // Remove once resolved.
          pendingRefDeltas.RemoveAt(i);
          progress = true;
        }
      }

      for (int i = pendingOffsetDeltas.Count - 1; i >= 0; i--)
      {
        PendingOffsetDelta pending = pendingOffsetDeltas[i];
        // Try to resolve using the base offset.
        if (TryApplyOffsetDelta(objectsByOffset, pending.BaseOffset, pending.BaseOffsetAlt, pending.DeltaData, out int baseType, out byte[] resolved))
        {
          StoreObject(objectsByHash, baseType, resolved);
          // Store by offset so other deltas can reference this later.
          objectsByOffset[pending.ObjectOffset] = new GitObject(baseType, resolved);
          // Remove once resolved.
          pendingOffsetDeltas.RemoveAt(i);
          progress = true;
        }
      }
    }
  }

  static byte[] InflateStandardObject(byte[] rawData, int offset, long uncompressedSize, out int bytesConsumed)
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

  static byte[] InflateDeltaObject(byte[] rawData, int offset, out int bytesConsumed)
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

  static byte[] InflateDeltaWithStream(byte[] rawData, int offset, bool useZlib, out int bytesConsumed)
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

  static bool TryApplyRefDelta(
    Dictionary<string, GitObject> objectsByHash,
    string baseHash,
    byte[] deltaData,
    out int baseType,
    out byte[] resolved)
  {
    baseType = 0;
    resolved = Array.Empty<byte>();

    if (!objectsByHash.TryGetValue(baseHash, out var baseObject))
    {
      Log($"RefDelta base not found: {baseHash}");
      return false;
    }

    int index = 0;
    if (!TryApplyDelta(baseObject.Data, deltaData, ref index, out byte[] output))
    {
      Log($"RefDelta apply failed for base {baseHash}");
      return false;
    }

    baseType = baseObject.Type;
    resolved = output;
    return true;
  }

  static long ReadDeltaSize(byte[] data, ref int index)
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

  static bool TryApplyOffsetDelta(
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
      Log($"OFSDelta base not found: {baseOffset} or {baseOffsetAlt}");
      return false;
    }

    int index = 0;
    if (!TryApplyDelta(baseObject.Data, deltaData, ref index, out byte[] output))
    {
      Log($"OFSDelta apply failed for base {baseOffset} or {baseOffsetAlt}");
      return false;
    }

    baseType = baseObject.Type;
    resolved = output;
    return true;
  }

  static bool TryApplyDelta(byte[] baseData, byte[] deltaData, ref int index, out byte[] output)
  {
    output = Array.Empty<byte>();

    long sourceSize = ReadDeltaSize(deltaData, ref index);
    long targetSize = ReadDeltaSize(deltaData, ref index);

    if (sourceSize != baseData.Length)
    {
      Log("Delta source size mismatch; continuing anyway.");
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
        // ADD: lower 7 bits specify how many literal bytes to copy from delta stream.
        int addSize = cmd & 0x7F;
        if (addSize > 0)
        {
          if (index + addSize > deltaData.Length || outPos + addSize > result.Length)
          {
            return false;
          }

          // Copy literal bytes directly from the delta stream.
          Buffer.BlockCopy(deltaData, index, result, outPos, addSize);
          index += addSize;
          outPos += addSize;
        }
      }
    }

    // Ensure we produced exactly the target size.
    if (outPos != targetSize)
    {
      return false;
    }

    // Return the fully reconstructed object.
    output = result;
    return true;
  }

  static bool TryParseDeltaCompletion(byte[] data, int length, out int consumedBytes)
  {
    consumedBytes = 0;
    int index = 0;

    if (!TryReadDeltaSize(data, length, ref index, out long sourceSize))
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

  static bool TryReadDeltaSize(byte[] data, int length, ref int index, out long size)
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

  static string ReadRefDeltaBaseHash(byte[] data, ref int index)
  {
    // REF_DELTA prefix contains the 20-byte base object hash.
    if (index + 20 > data.Length)
    {
      throw new InvalidDataException("Truncated REF_DELTA base hash.");
    }

    // Capture the 20-byte SHA1 bytes.
    byte[] hashBytes = data[index..(index + 20)];
    // Move the index past the hash.
    index += 20;
    // Convert to lowercase hex for lookups.
    return Convert.ToHexString(hashBytes).ToLower();
  }

  static int ReadOffsetDeltaDistance(byte[] data, ref int index)
  {
    // OFS_DELTA encodes a backwards distance using a special varint format.
    int value = 0;
    // Read the first byte of the distance.
    byte b = data[index++];
    // Low 7 bits are the value start.
    value = b & 0x7F;
    while ((b & 0x80) != 0 && index < data.Length)
    {
      // Subsequent bytes extend the value with a base-128 variant.
      b = data[index++];
      value = ((value + 1) << 7) | (b & 0x7F);
    }

    // This is the distance to subtract from the base offset.
    return value;
  }

  static void StoreObject(Dictionary<string, GitObject> objectsByHash, int type, byte[] data)
  {
    // Store a loose object on disk and in the in-memory map.
    string typeName = GetTypeName(type);
    // Create the canonical "type size\\0" header + body.
    byte[] headered = SharedUtils.AddHeaderString(data, typeName);
    // Hash is computed over the header + body.
    string hash = SharedUtils.CreateBlobHash(headered);
    // Build the loose object path from the hash.
    string blobPath = SharedUtils.CreateBlobPath(hash);
    // Compress and write the object file.
    SharedUtils.SaveBlobContent(headered, blobPath);
    // Cache the object body for later lookups.
    objectsByHash[hash] = new GitObject(type, data);

    if (type == (int)GitType.Tree)
    {
      Log($"Stored tree {hash} ({data.Length} bytes)");
    }
  }

  static bool TryGetObjectByHash(Dictionary<string, GitObject> objectsByHash, string hash, out GitObject obj)
  {
    if (objectsByHash.TryGetValue(hash, out obj))
    {
      Log($"Object {hash} found in memory");
      return true;
    }

    string path = SharedUtils.CreateBlobPath(hash);
    if (!File.Exists(path))
    {
      Log($"Object {hash} not found on disk at {path}");
      obj = default;
      return false;
    }

    byte[] fullObject = SharedUtils.ReadZLibFileToBytes(path);
    int spaceIndex = Array.IndexOf(fullObject, (byte)' ');
    int nullIndex = Array.IndexOf(fullObject, (byte)0, spaceIndex + 1);
    if (spaceIndex < 0 || nullIndex < 0)
    {
      obj = default;
      return false;
    }

    string typeName = Encoding.UTF8.GetString(fullObject, 0, spaceIndex);
    int type = GetTypeFromName(typeName);
    byte[] body = fullObject[(nullIndex + 1)..];
    obj = new GitObject(type, body);
    objectsByHash[hash] = obj;
    Log($"Object {hash} loaded from disk ({typeName})");
    return true;
  }

  static int GetTypeFromName(string typeName)
  {
    return typeName switch
    {
      "commit" => (int)GitType.Commit,
      "tree" => (int)GitType.Tree,
      "blob" => (int)GitType.Blob,
      "tag" => (int)GitType.Tag,
      _ => 0
    };
  }

  static string StripPktLinePrefix(string line)
  {
    // pkt-line format prefixes each line with 4 hex digits for length.
    if (line.Length >= 4 && line.Take(4).All(IsHexDigit))
    {
      // Drop the length prefix and return the payload.
      return line[4..];
    }
    // If it doesn't look like pkt-line, return unchanged.
    return line;
  }

  static bool IsHexDigit(char c)
  {
    // Accept both lowercase and uppercase hex digits.
    return (c >= '0' && c <= '9') ||
           (c >= 'a' && c <= 'f') ||
           (c >= 'A' && c <= 'F');
  }

  static string GetTypeName(int type) => type switch
  {
    // Map internal enum values to git's type strings.
    (int)GitType.Commit => "commit",
    (int)GitType.Tree => "tree",
    (int)GitType.Blob => "blob",
    (int)GitType.Tag => "tag",
    // Fallback for unexpected values.
    _ => "unknown"
  };

  enum GitType { Commit = 1, Tree = 2, Blob = 3, Tag = 4, OffsetDelta = 6, RefDelta = 7 }

  private readonly record struct PendingRefDelta(string BaseHash, byte[] DeltaData, int ObjectOffset);

  private readonly record struct PendingOffsetDelta(int BaseOffset, int? BaseOffsetAlt, byte[] DeltaData, int ObjectOffset);

  private readonly record struct GitObject(int Type, byte[] Data);

  static bool TryGetBaseObject(
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

  static void Log(string message)
  {
    if (DebugLog)
    {
      Console.WriteLine(message);
    }
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

