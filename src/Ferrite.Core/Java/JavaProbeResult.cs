namespace Ferrite.Core.Java;

public sealed record JavaProbeResult(
    string? VersionText,
    int? MajorVersion,
    string? Vendor,
    string? Home,
    string? Architecture,
    bool Is64Bit);
