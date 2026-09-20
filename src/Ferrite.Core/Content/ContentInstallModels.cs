namespace Ferrite.Core.Content;

/// <summary>One file an install plan will place into the instance.</summary>
public sealed record ContentInstallItem(
    string ProjectId,
    string VersionId,
    string FileName,
    string Url,
    long Size,
    string? Sha1,
    string? Sha512,
    ContentProjectType ProjectType,
    string TargetFolder);

public sealed record ContentInstallPlan(
    string RootProjectId,
    IReadOnlyList<ContentInstallItem> Items,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ContentVersion> OptionalDependencies);

public sealed record ContentInstallResult(
    int Installed,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Warnings);
