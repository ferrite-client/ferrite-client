using Ferrite.Core.Java;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Storage;

namespace Ferrite.Core.Tests;

/// <summary>
/// Which runtime a launch uses. The order matters: an instance that names a runtime must not be
/// overruled by the launcher default, and the default must not be silently ignored either.
/// </summary>
public sealed class JavaSelectionPrecedenceTests
{
    private static JavaRuntime Runtime(int major, string name) => new()
    {
        ExecutablePath = $@"C:\java\{name}\bin\java.exe",
        MajorVersion = major,
        Version = new Version(major, 0, 0),
        Vendor = name,
        Source = JavaRuntimeSource.CommonLocation,
    };

    private static readonly JavaRuntime Java17 = Runtime(17, "semeru17");
    private static readonly JavaRuntime Java21 = Runtime(21, "temurin21");
    private static readonly JavaRuntime Java25 = Runtime(25, "adoptium25");
    private static readonly IReadOnlyList<JavaRuntime> Installed = [Java17, Java21, Java25];

    [Fact]
    public void The_instances_own_runtime_wins_over_the_launcher_default()
    {
        var instance = new InstanceRecord { JavaPath = Java17.ExecutablePath };

        var selected = InstanceLauncher.SelectRuntime(
            instance,
            Java25.ExecutablePath,
            Installed,
            required: 21);

        Assert.Equal(Java17.ExecutablePath, selected?.ExecutablePath);
    }

    [Fact]
    public void The_launcher_default_is_used_when_the_instance_names_none()
    {
        var instance = new InstanceRecord();

        var selected = InstanceLauncher.SelectRuntime(
            instance,
            Java25.ExecutablePath,
            Installed,
            required: 21);

        Assert.Equal(Java25.ExecutablePath, selected?.ExecutablePath);
    }

    [Fact]
    public void A_preference_that_is_not_installed_falls_back_to_the_best_fit()
    {
        var instance = new InstanceRecord { JavaPath = @"C:\java\gone\bin\java.exe" };

        var selected = InstanceLauncher.SelectRuntime(
            instance,
            @"C:\java\also-gone\bin\java.exe",
            Installed,
            required: 21);

        // The best fit for Java 21 is a runtime that satisfies it, not an arbitrary first entry.
        Assert.NotNull(selected);
        Assert.True(
            selected!.MajorVersion >= 21,
            $"selected Java {selected.MajorVersion}, which does not satisfy Java 21");
    }

    [Fact]
    public void Nothing_installed_selects_nothing()
    {
        var selected = InstanceLauncher.SelectRuntime(
            new InstanceRecord { JavaPath = Java17.ExecutablePath },
            Java25.ExecutablePath,
            [],
            required: 21);

        Assert.Null(selected);
    }
}
