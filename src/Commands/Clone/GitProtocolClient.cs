namespace Commands;

internal sealed class GitProtocolClient(HttpClient client)
{
  public async Task<(string sha1, string headRef)> DiscoverReferences(string repositoryUrl)
  {
    string url = $"{repositoryUrl.TrimEnd('/')}/info/refs?service=git-upload-pack";

    var request = new HttpRequestMessage(HttpMethod.Get, url);
    request.Headers.Add("User-Agent", "git/2.0.0");
    request.Headers.Add("Accept", "*/*");

    HttpResponseMessage response = await client.SendAsync(request);
    response.EnsureSuccessStatusCode();

    string content = await response.Content.ReadAsStringAsync();

    var lines = content.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
    string? headSha = null;
    string? headRef = null;

    foreach (var line in lines)
    {
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

    return (headSha ?? "", headRef ?? "refs/heads/master");
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
