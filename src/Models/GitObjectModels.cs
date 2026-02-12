namespace codecrafters_git.src.Models;

public enum GitType
{
  Commit = 1,
  Tree = 2,
  Blob = 3,
  Tag = 4,
  OffsetDelta = 6,
  RefDelta = 7
}

public readonly record struct PendingRefDelta(string BaseHash, byte[] DeltaData, int ObjectOffset);

public readonly record struct PendingOffsetDelta(int BaseOffset, int? BaseOffsetAlt, byte[] DeltaData, int ObjectOffset);

public readonly record struct GitObject(int Type, byte[] Data);
