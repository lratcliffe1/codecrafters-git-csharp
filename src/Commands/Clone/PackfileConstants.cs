namespace codecrafters_git.src.Commands.Clone;

public static class PackfileConstants
{
  // Packfile format constants
  public const int PACKFILE_HEADER_SIZE = 12; // 4 bytes "PACK" + 4 bytes version + 4 bytes object count
  public const int OBJECT_COUNT_OFFSET = 8; // Offset where object count is stored
  public const int OBJECT_DATA_START_OFFSET = 12; // Where object data starts after header
  public const int SHA1_HASH_SIZE = 20; // Size of SHA-1 hash in bytes
  public const int PACKFILE_CHECKSUM_SIZE = 20; // Size of packfile checksum at end

  // Object header bit flags
  public const byte OBJECT_SIZE_CONTINUATION_FLAG = 0x80; // Bit 7: more size bytes follow
  public const byte OBJECT_SIZE_MASK = 0x7F; // Bits 0-6: size value in variable-length encoding
  public const byte OBJECT_TYPE_MASK = 0x70; // Bits 4-6: object type
  public const byte OBJECT_SIZE_LOW_BITS = 0x0F; // Bits 0-3: low 4 bits of size
  public const int OBJECT_TYPE_SHIFT = 4; // Shift to extract type from header byte
  public const int OBJECT_SIZE_SHIFT_START = 4; // Initial shift for size encoding
  public const int OBJECT_SIZE_SHIFT_INCREMENT = 7; // Shift increment per continuation byte

  // Variable-length encoding constants (used in multiple places)
  public const byte VAR_LEN_CONTINUATION_FLAG = 0x80; // Bit 7: more bytes follow
  public const byte VAR_LEN_VALUE_MASK = 0x7F; // Bits 0-6: value bits

  // Delta command bit flags
  public const byte COPY_COMMAND_FLAG = 0x80; // Bit 7: indicates COPY command
  public const byte ADD_SIZE_MASK = 0x7F; // Bits 0-6: mask for ADD command size

  // Copy offset byte flags (bits 0-3)
  public const byte COPY_OFFSET_BYTE_0 = 0x01; // Bit 0: copy offset byte 0 present
  public const byte COPY_OFFSET_BYTE_1 = 0x02; // Bit 1: copy offset byte 1 present
  public const byte COPY_OFFSET_BYTE_2 = 0x04; // Bit 2: copy offset byte 2 present
  public const byte COPY_OFFSET_BYTE_3 = 0x08; // Bit 3: copy offset byte 3 present

  // Copy size byte flags (bits 4-6)
  public const byte COPY_SIZE_BYTE_0 = 0x10; // Bit 4: copy size byte 0 present
  public const byte COPY_SIZE_BYTE_1 = 0x20; // Bit 5: copy size byte 1 present
  public const byte COPY_SIZE_BYTE_2 = 0x40; // Bit 6: copy size byte 2 present

  // Default copy size when copySize is 0
  public const int DEFAULT_COPY_SIZE = 0x10000; // 64KB
}
