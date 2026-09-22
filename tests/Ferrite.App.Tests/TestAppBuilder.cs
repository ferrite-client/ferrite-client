using Avalonia;
using Avalonia.Headless;
using Ferrite.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Ferrite.App.Tests;

/// <summary>
/// Headless Avalonia host used by the UI tests. It renders the real application and real views, so a
/// passing test is evidence about the shipped interface rather than a mock.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<Ferrite.App.App>()
        .WithInterFont()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
