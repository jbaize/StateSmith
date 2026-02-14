using FluentAssertions;
using Spectre.Console.Testing;
using StateSmith.Cli.Run;
using StateSmith.Runner;
using System.IO;
using Xunit;

namespace StateSmith.CliTest.Run;

public class DiagramRunnerMultiSmTests
{
    [Fact]
    public void DrawIoFileWithMultipleStateMachinesRunsAndSharesEvents()
    {
        string sourceDir = ExamplesHelper.GetExamplesDir();
        string sourceDiagram = Path.Combine(sourceDir, "2-sm.drawio");

        string testDir = Path.Combine(Path.GetTempPath(), "statesmith-cli-test-" + Path.GetRandomFileName());
        Directory.CreateDirectory(testDir);
        string testDiagram = Path.Combine(testDir, "2-sm.drawio");
        File.Copy(sourceDiagram, testDiagram);

        try
        {
            var diagramRunner = new DiagramRunner(
            runConsole: new RunConsole(new TestConsole()),
            diagramOptions: new DiagramOptions(lang: TranspilerId.C99, noSimGen: true),
            searchDirectory: testDir,
            runHandlerOptions: new RunHandlerOptions(currentDirectory: testDir));

            var runInfoStore = new RunInfoStore(testDir);
            diagramRunner.RunDiagramFile("2-sm.drawio", testDiagram, out bool diagramRan, runInfoStore);

            diagramRan.Should().BeTrue();

            var alphaHeader = File.ReadAllText(Path.Combine(testDir, "AlphaSm.h"));
            var betaHeader = File.ReadAllText(Path.Combine(testDir, "BetaSm.h"));

            alphaHeader.Should().Contain("ALPHA_EVT");
            alphaHeader.Should().Contain("BETA_EVT");

            betaHeader.Should().Contain("ALPHA_EVT");
            betaHeader.Should().Contain("BETA_EVT");
        }
        finally
        {
            Directory.Delete(testDir, recursive: true);
        }
    }
}
