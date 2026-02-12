namespace codecrafters_git.src.Models;

public class LsTreeRow(string mode, string hash, string name)
{
    public string Mode { get; set; } = mode;
    public string Hash { get; set; } = hash;
    public string Name { get; set; } = name;
    public string ModeName { get; set; } = mode switch
    {
        "40000" or "040000" => "tree",
        "100644" or "100755" or "120000" => "blob",
        "160000" => "commit",
        _ => "unknown"
    };
}
