using System.IO.Compression;
using System.Text;

namespace Helpers;

public class SharedUtils()
{
  public static string CreateBlobPath(string hash)
  {
    string pathBase = ".git/objects/";
    string hash0_1 = hash[0..2];
    string hash2_40 = hash[2..];

    return $"{pathBase}/{hash0_1}/{hash2_40}";
  }
}