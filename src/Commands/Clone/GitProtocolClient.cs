namespace Commands;

internal sealed class GitProtocolClient(HttpClient client)
{
  public async Task<(string Sha, string HeadRef)> DiscoverReferences(string repositoryUrl)
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
        string line = PktLineParser.StripPktLinePrefix(rawLine);
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

  public async Task<byte[]> FetchPackfile(string repositoryUrl, string sha1)
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
}

internal static class PktLineParser
{
  internal static string StripPktLinePrefix(string line)
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

  private static bool IsHexDigit(char c)
  {
    // Accept both lowercase and uppercase hex digits.
    return (c >= '0' && c <= '9') ||
           (c >= 'a' && c <= 'f') ||
           (c >= 'A' && c <= 'F');
  }
}
