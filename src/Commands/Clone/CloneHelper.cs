using codecrafters_git.src.Services;

namespace codecrafters_git.src.Commands.Clone;

public interface ICloneHelper
{
  Task<string> Clone(string repositoryUrl, string targetDirectory);
}

public class CloneService(
  IGitProtocolClient gitProtocolClient,
  IObjectStore objectStore,
  IPackfileParser packfileParser,
  ITreeHelper treeHelper,
  IInitHelper initHelper,
  IGitRefHelper gitRefHelper) : ICloneHelper
{
  public async Task<string> Clone(string repositoryUrl, string targetDirectory)
  {
    initHelper.Init(targetDirectory + "/");

    (string headCommitSha, string headRef) = await gitProtocolClient.DiscoverReferences(repositoryUrl);
    byte[] packfileData = await gitProtocolClient.FetchPackfile(repositoryUrl, headCommitSha);

    string originalWorkingDirectory = Directory.GetCurrentDirectory();
    string workTreeRoot = Path.Combine(originalWorkingDirectory, targetDirectory);
    Directory.SetCurrentDirectory(targetDirectory);

    try
    {
      byte[] packfileWithoutHeader = packfileParser.RemovePackHeader(packfileData);
      packfileParser.ParseAllObjects(packfileWithoutHeader);

      gitRefHelper.WriteHead(headRef);
      gitRefHelper.WriteRef(headRef, headCommitSha);

      treeHelper.CheckoutWorkingTree(objectStore, workTreeRoot, headCommitSha);
    }
    finally
    {
      Directory.SetCurrentDirectory(originalWorkingDirectory);
    }

    return "";
  }
}
