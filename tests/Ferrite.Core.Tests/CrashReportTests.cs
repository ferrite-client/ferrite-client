using Ferrite.Core.Content;
using Ferrite.Core.Diagnostics;

namespace Ferrite.Core.Tests;

/// <summary>
/// Crash report parsing and mod attribution, using reports shaped like the ones Fabric, NeoForge,
/// and vanilla actually write.
/// </summary>
public sealed class CrashReportTests
{
    private const string FabricReport = """
---- Minecraft Crash Report ----
// Don't be sad, have a hug! <3

Time: 2026-09-20 21:14:03
Description: Rendering overlay

java.lang.NullPointerException: Cannot invoke "RenderType.bufferSize()" because "type" is null
	at net.minecraft.client.renderer.SectionRenderDispatcher.render(SectionRenderDispatcher.java:412)
	at net.caffeinemc.mods.sodium.client.render.SodiumWorldRenderer.render(SodiumWorldRenderer.java:187)
	at net.minecraft.client.Minecraft.runTick(Minecraft.java:1123)
	at java.base/java.lang.Thread.run(Thread.java:1583)

A detailed walkthrough of the error, its code path and all known details is as follows:
---------------------------------------------------------------------------------------

-- Head --
Thread: Render thread
Stacktrace:
	at net.minecraft.client.renderer.SectionRenderDispatcher.render(SectionRenderDispatcher.java:412)
	at net.caffeinemc.mods.sodium.client.render.SodiumWorldRenderer.render(SodiumWorldRenderer.java:187)

-- System Details --
Details:
	Minecraft Version: 1.21.1
	Minecraft Version ID: 1.21.1
	Operating System: Windows 11 (amd64) version 10.0
	Java Version: 21.0.5, Microsoft
	Memory: 1073741824 bytes (1024 MiB) / 2147483648 bytes (2048 MiB) up to 4294967296 bytes (4096 MiB)
	Suspected Mods: sodium
	Fabric Mods:
		fabric-api: Fabric API 0.115.0+1.21.1
		sodium: Sodium 0.6.0+mc1.21.1
		lithium: Lithium 0.14.0+mc1.21.1
""";

    private const string NeoForgeReport = """
---- Minecraft Crash Report ----
// Why did you do that?

Time: 2026-09-20 21:20:11
Description: Ticking block entity

java.lang.IllegalStateException: Missing capability
	at com.example.examplemod.machine.MachineTick.tick(MachineTick.java:88)
	at net.minecraft.world.level.Level.tickBlockEntities(Level.java:901)

-- System Details --
Details:
	Minecraft Version: 1.21.1
	NeoForge Version: 21.1.72

-- Mod List --
	Mods: 3
		examplemod: Example Machines 1.2.0
		neoforge: NeoForge 21.1.72
		jei: Just Enough Items 19.0.0.1
""";

    [Fact]
    public void A_fabric_report_parses_its_headline_fields()
    {
        var report = CrashReportParser.Parse("crash-2026-09-20_21.14.03-client.txt", FabricReport);

        Assert.True(CrashReportParser.LooksLikeCrashReport(FabricReport));
        Assert.Equal("Rendering overlay", report.Description);
        Assert.Equal(2026, report.Time?.Year);
        Assert.Equal("java.lang.NullPointerException", report.ExceptionType);
        Assert.Equal("Cannot invoke \"RenderType.bufferSize()\" because \"type\" is null", report.ExceptionMessage);
        Assert.Equal("1.21.1", report.SystemDetails["Minecraft Version"]);
        Assert.Contains("sodium", report.SuspectedMods);
    }

    [Fact]
    public void Stack_frames_carry_class_file_and_line()
    {
        var report = CrashReportParser.Parse("fabric.txt", FabricReport);

        var sodium = report.Frames.First(frame =>
            frame.ClassName == "net.caffeinemc.mods.sodium.client.render.SodiumWorldRenderer");
        Assert.Equal("render", sodium.MethodName);
        Assert.Equal("SodiumWorldRenderer.java", sodium.FileName);
        Assert.Equal(187, sodium.LineNumber);
    }

    [Fact]
    public void A_fabric_mod_list_is_read()
    {
        var report = CrashReportParser.Parse("fabric.txt", FabricReport);

        Assert.Contains("sodium: Sodium 0.6.0+mc1.21.1", report.ListedMods);
        Assert.Contains("lithium: Lithium 0.14.0+mc1.21.1", report.ListedMods);
    }

    [Fact]
    public void A_neoforge_mod_list_section_is_read()
    {
        var report = CrashReportParser.Parse("neoforge.txt", NeoForgeReport);

        Assert.Equal("Ticking block entity", report.Description);
        Assert.Equal("java.lang.IllegalStateException", report.ExceptionType);
        Assert.Equal("21.1.72", report.SystemDetails["NeoForge Version"]);
        Assert.Contains("examplemod: Example Machines 1.2.0", report.ListedMods);
    }

    [Fact]
    public void Text_that_is_not_a_crash_report_is_recognised()
    {
        Assert.False(CrashReportParser.LooksLikeCrashReport("just a log line"));
        Assert.False(CrashReportParser.LooksLikeCrashReport(null));
    }

    [Fact]
    public void Attribution_matches_stack_frames_to_installed_mods()
    {
        var report = CrashReportParser.Parse("fabric.txt", FabricReport);
        var mods = new List<ModMetadata>
        {
            Mod("sodium-fabric-0.6.0+mc1.21.1.jar", "sodium", "Sodium"),
            Mod("lithium-fabric-0.14.0.jar", "lithium", "Lithium"),
            Mod("fabric-api-0.115.0.jar", "fabric-api", "Fabric API"),
        };

        var analysis = CrashAnalyzer.Analyze(report, mods);

        var attributed = Assert.Single(analysis.AttributedMods);
        Assert.Equal("sodium", attributed.ModId);
        Assert.Equal("Sodium", attributed.DisplayName);
        Assert.Contains(attributed.Frames, frame => frame.Contains("SodiumWorldRenderer", StringComparison.Ordinal));
        Assert.Contains("sodium", analysis.SuspectedByReport);
        Assert.Empty(analysis.MissingMods);
    }

    [Fact]
    public void Game_and_loader_frames_are_never_attributed_to_a_mod()
    {
        var report = CrashReportParser.Parse("fabric.txt", FabricReport);
        var mods = new List<ModMetadata> { Mod("fabric-api-0.115.0.jar", "fabric-api", "Fabric API") };

        var analysis = CrashAnalyzer.Analyze(report, mods);

        // The only mod here ships a "fabric" token, but every "fabric" frame belongs to the loader.
        Assert.Empty(analysis.AttributedMods);
    }

    [Fact]
    public void A_mod_listed_in_the_report_but_not_installed_is_reported()
    {
        var report = CrashReportParser.Parse("fabric.txt", FabricReport);
        var mods = new List<ModMetadata> { Mod("sodium-fabric-0.6.0.jar", "sodium", "Sodium") };

        var analysis = CrashAnalyzer.Analyze(report, mods);

        Assert.Contains(analysis.MissingMods, entry => entry.StartsWith("lithium:", StringComparison.Ordinal));
        Assert.DoesNotContain(analysis.MissingMods, entry => entry.StartsWith("sodium:", StringComparison.Ordinal));
    }

    [Fact]
    public void The_rendered_analysis_states_what_was_found_and_what_it_means()
    {
        var report = CrashReportParser.Parse("fabric.txt", FabricReport);
        var mods = new List<ModMetadata> { Mod("sodium-fabric-0.6.0.jar", "sodium", "Sodium") };

        var text = CrashAnalysisText.Render(CrashAnalyzer.Analyze(report, mods));

        Assert.Contains("Rendering overlay", text, StringComparison.Ordinal);
        Assert.Contains("java.lang.NullPointerException", text, StringComparison.Ordinal);
        Assert.Contains("Sodium [sodium]", text, StringComparison.Ordinal);
        Assert.Contains("not proof of cause", text, StringComparison.Ordinal);
    }

    private static ModMetadata Mod(string fileName, string modId, string name) => new()
    {
        FilePath = Path.Combine("mods", fileName),
        FileName = fileName,
        ModId = modId,
        Name = name,
        Version = "1.0",
        Loader = "fabric",
    };
}
