using codecrafters_git.src.Services;

namespace codecrafters_git.src.Commands.Clone;

public interface ICloneHelper
{
  Task<string> Clone(string repositoryUrl, string targetDirectory);
}

public class CloneHelper(
  IGitProtocolClient gitProtocolClient,
  IObjectStore objectStore,
  IPackfileParser packfileParser,
  ITreeHelper treeHelper,
  IInitHelper initHelper,
  IRepository repository,
  IGitRefHelper gitRefHelper) : ICloneHelper
{
  public async Task<string> Clone(string repositoryUrl, string targetDirectory)
  {
    string workTreeRoot = Path.GetFullPath(Path.Combine(repository.WorkTreeRoot, targetDirectory));
    repository.UseRepositoryRoot(workTreeRoot);

    initHelper.Init(workTreeRoot);

    (string headCommitSha, string headRef) = await gitProtocolClient.DiscoverReferences(repositoryUrl);
    byte[] packfileData = await gitProtocolClient.FetchPackfile(repositoryUrl, headCommitSha);

    int packOffset = packfileParser.FindPackOffset(packfileData);
    packfileParser.ParseAllObjects(packfileData, packOffset);

    gitRefHelper.WriteHead(headRef);
    gitRefHelper.WriteRef(headRef, headCommitSha);

    treeHelper.CheckoutWorkingTree(objectStore, workTreeRoot, headCommitSha);

    repository.UseRepositoryRoot(repository.WorkTreeRoot);

    return "";
  }
}
