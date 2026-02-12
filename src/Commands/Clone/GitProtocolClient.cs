namespace codecrafters_git.src.Commands.Clone;

public interface IGitProtocolClient
{
  Task<(string sha1, string headRef)> DiscoverReferences(string repositoryUrl);
  Task<byte[]> FetchPackfile(string repositoryUrl, string sha1);
}

public class GitProtocolClient(HttpClient client) : IGitProtocolClient
{
  public async Task<(string sha1, string headRef)> DiscoverReferences(string repositoryUrl)
  {
    string infoRefsUrl = $"{repositoryUrl.TrimEnd('/')}/info/refs?service=git-upload-pack";

    var request = new HttpRequestMessage(HttpMethod.Get, infoRefsUrl);
    request.Headers.Add("User-Agent", "git/2.0.0");
    request.Headers.Add("Accept", "*/*");

    HttpResponseMessage response = await client.SendAsync(request);
    response.EnsureSuccessStatusCode();

    string responseContent = await response.Content.ReadAsStringAsync();
    (string? headCommitSha, string? headBranchRef) = ParseInfoRefsResponse(responseContent);

    return (headCommitSha ?? "", headBranchRef ?? "refs/heads/master");
  }

  public async Task<byte[]> FetchPackfile(string repositoryUrl, string sha1)
  {
    string uploadPackUrl = $"{repositoryUrl.TrimEnd('/')}/git-upload-pack";

    string requestBody = $"0032want {sha1}\n00000009done\n";

    var request = new HttpRequestMessage(HttpMethod.Post, uploadPackUrl)
    {
      Content = new StringContent(requestBody)
    };

    request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-git-upload-pack-request");

    HttpResponseMessage response = await client.SendAsync(request);
    response.EnsureSuccessStatusCode();

    return await response.Content.ReadAsByteArrayAsync();
  }

  private static (string? headCommitSha, string? headBranchRef) ParseInfoRefsResponse(string responseContent)
  {
    var responseLines = responseContent.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
    string? headCommitSha = null;
    string? headBranchRef = null;

    foreach (string line in responseLines)
    {
      headBranchRef ??= ParseSymrefFromLine(line);
      headCommitSha ??= ParseHeadCommitFromLine(line);
    }

    return (headCommitSha, headBranchRef);
  }

  private static string? ParseSymrefFromLine(string line)
  {
    int symrefStart = line.IndexOf("symref=HEAD:", StringComparison.Ordinal);
    if (symrefStart < 0)
    {
      return null;
    }

    string symrefValue = line[(symrefStart + "symref=HEAD:".Length)..];
    int nullTerminatorIndex = symrefValue.IndexOf('\0');
    return nullTerminatorIndex >= 0 ? symrefValue[..nullTerminatorIndex] : symrefValue;
  }

  private static string? ParseHeadCommitFromLine(string line)
  {
    int headMarkerIndex = line.IndexOf(" HEAD", StringComparison.Ordinal);
    if (headMarkerIndex < 40)
    {
      return null;
    }

    return line.Substring(headMarkerIndex - 40, 40);
  }
}
