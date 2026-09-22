namespace Ferrite.App.ViewModels;

/// <summary>
/// One row of library cards. The grid is expressed as rows so a virtualising panel still applies:
/// only the rows inside the viewport are realised, however many instances the library holds.
/// </summary>
public sealed record InstanceRowViewModel(IReadOnlyList<InstanceCardViewModel> Cards);
