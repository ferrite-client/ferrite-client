using Ferrite.Core.Java;
using Ferrite.Core.Loaders;
using Ferrite.Core.Platform;

namespace Ferrite.Core.Tests;

/// <summary>
/// Two defects found by running loader installation and Java provisioning live, kept as regression
/// tests because neither was visible to the tests that existed at the time.
/// </summary>
public sealed class LoaderInstallTests : IDisposable
{
    private readonly string _root;

    public LoaderInstallTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ferrite-loader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// Forge's install profile lists a processor's dependencies in "classpath" but not the processor
    /// jar itself. Omitting it meant the JVM was asked to run a class that was not on the classpath,
    /// which failed with "Could not find or load main class net.minecraftforge.installertools.ConsoleTool".
    /// </summary>
    [Fact]
    public void A_processor_jar_is_always_on_its_own_classpath()
    {
        var jar = CreateFile("installertools-1.4.3.jar");
        var gson = CreateFile("gson-2.10.1.jar");
        var srgutils = CreateFile("srgutils-0.5.10.jar");

        var classpath = ForgeProcessorRunner.BuildClasspath(jar, [gson, srgutils]);

        Assert.Equal(jar, classpath[0]);
        Assert.Equal(3, classpath.Count);
        Assert.Contains(gson, classpath);
        Assert.Contains(srgutils, classpath);
    }

    [Fact]
    public void A_classpath_that_already_names_the_processor_jar_does_not_duplicate_it()
    {
        var jar = CreateFile("installertools-1.4.3.jar");
        var gson = CreateFile("gson-2.10.1.jar");

        var classpath = ForgeProcessorRunner.BuildClasspath(jar, [jar, gson]);

        Assert.Equal(2, classpath.Count);
        Assert.Single(classpath, entry => string.Equals(entry, jar, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_classpath_entry_that_is_not_downloaded_is_left_out()
    {
        var jar = CreateFile("binarypatcher-1.2.0.jar");

        var classpath = ForgeProcessorRunner.BuildClasspath(
            jar,
            [Path.Combine(_root, "never-downloaded.jar")]);

        Assert.Equal([jar], classpath);
    }

    /// <summary>
    /// Mojang's runtime catalog is keyed by architecture ("windows-x64"), so a bare OS name matched
    /// nothing and every provisioning attempt reported "no published runtime".
    /// </summary>
    [Fact]
    public void The_runtime_catalog_key_names_the_architecture()
    {
        var key = JavaProvisioner.OsKey();

        // Every key Mojang actually publishes for the three platforms the launcher supports.
        Assert.Contains(key, new[]
        {
            "windows-x64", "windows-x86", "windows-arm64",
            "linux", "linux-i386",
            "mac-os", "mac-os-arm64",
        });

        if (PlatformInfo.IsWindows && PlatformInfo.Arch == CpuArchitecture.X64)
        {
            Assert.Equal("windows-x64", key);
        }
    }

    private string CreateFile(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }
}
