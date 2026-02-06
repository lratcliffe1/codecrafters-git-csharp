namespace Commands;

internal enum GitType
{
  Commit = 1,
  Tree = 2,
  Blob = 3,
  Tag = 4,
  OffsetDelta = 6,
  RefDelta = 7
}

internal readonly record struct PendingRefDelta(string BaseHash, byte[] DeltaData, int ObjectOffset);

internal readonly record struct PendingOffsetDelta(int BaseOffset, int? BaseOffsetAlt, byte[] DeltaData, int ObjectOffset);

internal readonly record struct GitObject(int Type, byte[] Data);
