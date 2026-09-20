using System.Text.Json;
using Ferrite.Core.Java;
using Ferrite.Core.Json;
using Ferrite.Core.Minecraft;
using Ferrite.Core.Storage;
using Ferrite.Core.Tests.Infrastructure;

namespace Ferrite.Core.Tests;

public sealed class LaunchCommandBuilderTests
{
    private const string AccessToken = "eyJhbGciOiJIUzI1NiJ9.fake-token-value.signature";

    [Fact]
    public void Modern_document_produces_a_complete_command()
    {
        var request = CreateRequest(VersionFixtures.ModernRelease);
        var command = new LaunchCommandBuilder().Build(request);

        Assert.Contains("net.minecraft.client.main.Main", command.Arguments);
        Assert.Contains("--username", command.Arguments);
        Assert.Contains("Steve", command.Arguments);
        Assert.Contains("--accessToken", command.Arguments);
        Assert.Contains(AccessToken, command.Arguments);
        Assert.Contains("-Djava.library.path=" + request.NativesDirectory + "/java", command.Arguments);
        Assert.Contains("-Dlog4j.configurationFile=" + request.Plan.LoggingConfigPath, command.Arguments);
    }

    [Fact]
    public void Classpath_lists_libraries_in_order_with_the_client_jar_last()
    {
        var command = new LaunchCommandBuilder().Build(CreateRequest(VersionFixtures.ModernRelease));

        var entries = command.Classpath.Split(Path.PathSeparator);
        Assert.Equal(3, entries.Length);
        Assert.EndsWith("demo-1.0.jar", entries[0], StringComparison.Ordinal);
        Assert.EndsWith("native-1.0-natives-windows.jar", entries[1], StringComparison.Ordinal);
        Assert.EndsWith("client.jar", entries[2], StringComparison.Ordinal);
    }

    [Fact]
    public void Default_user_jvm_applies_when_memory_is_not_overridden()
    {
        var command = new LaunchCommandBuilder().Build(CreateRequest(VersionFixtures.ModernRelease));

        Assert.Contains("-Xmx4G", command.Arguments);
        Assert.Contains("-Xms2G", command.Arguments);
        Assert.Contains("-XX:+UseStringDeduplication", command.Arguments);
    }

    [Fact]
    public void Instance_memory_overrides_default_user_jvm_memory_flags()
    {
        var instance = CreateInstance();
        instance.MemoryMb = 8192;
        instance.MinMemoryMb = 2048;

        var command = new LaunchCommandBuilder().Build(CreateRequest(VersionFixtures.ModernRelease, instance));

        Assert.Contains("-Xmx8192M", command.Arguments);
        Assert.Contains("-Xms2048M", command.Arguments);
        Assert.DoesNotContain("-Xmx4G", command.Arguments);
        Assert.DoesNotContain("-Xms2G", command.Arguments);
        Assert.Contains("-XX:+UseStringDeduplication", command.Arguments);
    }

    [Fact]
    public void Demo_mode_adds_the_demo_flag()
    {
        var instance = CreateInstance();
        instance.DemoMode = true;

        var command = new LaunchCommandBuilder().Build(CreateRequest(VersionFixtures.ModernRelease, instance));
        Assert.Contains("--demo", command.Arguments);
    }

    [Fact]
    public void Custom_resolution_adds_width_and_height()
    {
        var instance = CreateInstance();
        instance.WindowWidth = 1920;
        instance.WindowHeight = 1080;

        var command = new LaunchCommandBuilder().Build(CreateRequest(VersionFixtures.ModernRelease, instance));
        Assert.Contains("--width", command.Arguments);
        Assert.Contains("1920", command.Arguments);
        Assert.Contains("--height", command.Arguments);
        Assert.Contains("1080", command.Arguments);
    }

    [Fact]
    public void Quick_play_multiplayer_joins_a_server_when_requested()
    {
        var instance = CreateInstance();
        instance.LastServerAddress = "play.example.net";
        instance.LastServerPort = 25565;

        var request = CreateRequest(VersionFixtures.ModernRelease, instance, requestQuickPlayMultiplayer: true);
        var command = new LaunchCommandBuilder().Build(request);

        Assert.Contains("--quickPlayMultiplayer", command.Arguments);
        Assert.Contains("play.example.net:25565", command.Arguments);
    }

    [Fact]
    public void Quick_play_arguments_are_absent_when_not_requested()
    {
        var instance = CreateInstance();
        instance.LastServerAddress = "play.example.net";

        var command = new LaunchCommandBuilder().Build(CreateRequest(VersionFixtures.ModernRelease, instance));
        Assert.DoesNotContain("--quickPlayMultiplayer", command.Arguments);
    }

    [Fact]
    public void Display_arguments_redact_credentials_but_real_arguments_keep_them()
    {
        var command = new LaunchCommandBuilder().Build(CreateRequest(VersionFixtures.ModernRelease));

        Assert.Contains(AccessToken, command.Arguments);
        Assert.DoesNotContain(AccessToken, command.DisplayArguments);
        Assert.Contains("***redacted***", command.DisplayArguments);
        Assert.DoesNotContain(AccessToken, command.ToDisplayString());
    }

    [Fact]
    public void Legacy_document_gets_launcher_supplied_jvm_arguments()
    {
        var request = CreateRequest(VersionFixtures.LegacyRelease);
        var command = new LaunchCommandBuilder().Build(request);

        Assert.Contains("-Djava.library.path=" + request.NativesDirectory, command.Arguments);
        Assert.Contains("-cp", command.Arguments);
        Assert.Contains("--userType", command.Arguments);
        Assert.Contains("msa", command.Arguments);
        Assert.Contains("--username", command.Arguments);
    }

    [Fact]
    public void Unknown_placeholder_fails_closed()
    {
        var request = CreateRequest(VersionFixtures.ModernRelease, mutate: document =>
        {
            document.Arguments!.Game.Add(
                JsonDocument.Parse("\"--mystery ${not_a_real_placeholder}\"").RootElement.Clone());
        });

        Assert.Throws<VersionMetadataException>(() => new LaunchCommandBuilder().Build(request));
    }

    [Fact]
    public void Missing_main_class_is_reported_clearly()
    {
        var request = CreateRequest(VersionFixtures.ModernRelease, mutate: document => document.MainClass = null);

        var exception = Assert.Throws<VersionMetadataException>(() => new LaunchCommandBuilder().Build(request));
        Assert.Contains("main class", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static InstanceRecord CreateInstance() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test instance",
        MinecraftVersion = "26.3",
    };

    private static LaunchRequest CreateRequest(
        string documentJson,
        InstanceRecord? instance = null,
        bool requestQuickPlayMultiplayer = false,
        Action<VersionDocument>? mutate = null)
    {
        var document = JsonSerializer.Deserialize<VersionDocument>(documentJson, JsonDefaults.Remote)!;
        mutate?.Invoke(document);

        var workspace = Path.Combine(Path.GetTempPath(), "ferrite-launch-fixture");
        var libraries = new List<ResolvedLibrary>
        {
            new(
                new MavenCoordinates("com.example", "demo", "1.0"),
                Path.Combine(workspace, "libraries", "demo-1.0.jar"),
                null,
                IsNative: false),
            new(
                new MavenCoordinates("org.example", "native", "1.0", "natives-windows"),
                Path.Combine(workspace, "libraries", "native-1.0-natives-windows.jar"),
                null,
                IsNative: true),
        };

        var plan = new InstallPlan
        {
            VersionId = document.Id ?? "26.3",
            Document = document,
            Downloads = [],
            Libraries = libraries,
            Natives = [],
            Assets = [],
            ClientJarPath = Path.Combine(workspace, "versions", "client.jar"),
            LoggingConfigPath = Path.Combine(workspace, "versions", "client-logging.xml"),
        };

        return new LaunchRequest
        {
            Document = document,
            Plan = plan,
            Instance = instance ?? CreateInstance(),
            Account = new LaunchAccount
            {
                PlayerName = "Steve",
                Uuid = "069a79f4-44e9-4726-a5be-fca90e38aaf5",
                AccessToken = AccessToken,
                Xuid = "2535410000000000",
                ClientId = "00000000402b5328",
            },
            Java = new JavaRuntime
            {
                ExecutablePath = Path.Combine(workspace, "java.exe"),
                MajorVersion = 25,
            },
            GameDirectory = Path.Combine(workspace, "instance", "minecraft"),
            NativesDirectory = Path.Combine(workspace, "instance", "natives"),
            AssetsRoot = Path.Combine(workspace, "assets"),
            LibrariesDirectory = Path.Combine(workspace, "libraries"),
            LegacyAssetsDirectory = Path.Combine(workspace, "assets", "virtual", "legacy"),
            RequestQuickPlayMultiplayer = requestQuickPlayMultiplayer,
        };
    }
}
