using StateSmith.Output;
using StateSmith.Runner;
using StateSmith.SmGraph;
using StateSmith.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StateSmith.Cli.Run;

public class DiagramRunner
{
    // PUBLIC VAR! Feel free to clear it.
    public int warningCount = 0;

    private RunConsole _runConsole;
    private DiagramOptions _diagramOptions;

    private readonly string _searchDirectory;
    private readonly RunHandlerOptions _runHandlerOptions;
    private string CurrentDirectory => _runHandlerOptions.CurrentDirectory;

    public DiagramRunner(RunConsole runConsole, DiagramOptions diagramOptions, string searchDirectory, RunHandlerOptions runHandlerOptions)
    {
        _runConsole = runConsole;
        this._diagramOptions = diagramOptions;
        _searchDirectory = searchDirectory;
        this._runHandlerOptions = runHandlerOptions;
    }

    public void SetConsole(RunConsole runConsole)
    {
        _runConsole = runConsole;
    }

    public bool Run(List<string> targetDiagramFiles, RunInfoStore runInfoStore)
    {
        bool ranFiles = false;

        if (targetDiagramFiles.Count == 0)
        {
            _runConsole.MarkupLine("No diagrams found (that aren't already run by .csx).", filter: IsVerbose);
        }

        foreach (var diagramFile in targetDiagramFiles)
        {
            RunDiagramFileIfNeeded(diagramFile, runInfoStore, out var diagramRan);
            ranFiles |= diagramRan;
        }

        return ranFiles;
    }

    private bool IsVerbose => _runHandlerOptions.Verbose;
    private bool IsRebuild => _runHandlerOptions.Rebuild;

    public void RunDiagramFileIfNeeded(string diagramRelPath, RunInfoStore runInfoStore, out bool diagramRan, bool rebuildIfLastFailure = false)
    {
        string diagramLongerPath = $"{_searchDirectory}/{diagramRelPath}";
        string diagramAbsolutePath = Path.GetFullPath(diagramLongerPath);

        string? csxAbsPath = runInfoStore.FindCsxWithDiagram(diagramAbsolutePath);
        if (csxAbsPath != null)
        {
            var csxRelativePath = Path.GetRelativePath(_searchDirectory, csxAbsPath);
            _runConsole.QuietMarkupLine($"...Skipping diagram `{diagramRelPath}` already run by csx file `{csxRelativePath}`.", filter: IsVerbose);
            diagramRan = false;
            return;
        }

        _runConsole.AddMildHeader($"Checking diagram: `{diagramRelPath}`", filter: IsVerbose);
        _runConsole.WriteLine($"Diagram settings: {_diagramOptions.Describe()}", filter: IsVerbose);
        var incrementalRunChecker = new IncrementalRunChecker(_runConsole, _searchDirectory, IsVerbose, runInfoStore);

        if (incrementalRunChecker.TestDiagramOnlyFilePath(diagramAbsolutePath, rebuildIfLastFailure) != IncrementalRunChecker.Result.OkToSkip)
        {
            // already basically printed by IncrementalRunChecker
            //_console.WriteLine($"Script or its diagram dependencies have changed. Running script.");
        }
        else
        {
            if (IsRebuild)
            {
                _runConsole.MarkupLine("Would normally skip (file dates look good), but [yellow]rebuild[/] option set.", filter: IsVerbose);
            }
            else
            {
                _runConsole.QuietMarkupLine($"Diagram and its dependencies haven't changed. Skipping.", filter: IsVerbose);
                diagramRan = false;
                return; //!!!!!!!!!!! NOTE the return here.
            }
        }

        RunDiagramFile(diagramRelPath, diagramAbsolutePath, out diagramRan, runInfoStore);
    }

    public void RunDiagramFile(string shortPath, string absolutePath, out bool diagramRan, RunInfoStore runInfoStore)
    {
        string callerFilePath = CurrentDirectory + "/";  // Slash needed for fix of https://github.com/StateSmith/StateSmith/issues/345

        var info = new DiagramRunInfo(absolutePath: absolutePath);
        runInfoStore.diagramRuns[absolutePath] = info; // will overwrite if already exists

        _runConsole.WriteLine($"Running diagram: `{shortPath}`");

        var multiSmRunData = BuildMultiSmRunData(callerFilePath, absolutePath);
        if (multiSmRunData.count == 0)
        {
            multiSmRunData = new(count: 1, stateMachineNames: new(), sharedEvents: new());
        }

        diagramRan = false;

        for (int i = 0; i < multiSmRunData.count; i++)
        {
            var runnerSettings = BuildBaseRunnerSettings(absolutePath);
            runnerSettings.stateMachineName = multiSmRunData.stateMachineNames.ElementAtOrDefault(i);
            runnerSettings.simulation.enableGeneration = i == 0 && !_diagramOptions.NoSimGen;

            bool ranSingleSm = RunSingleStateMachine(callerFilePath, runnerSettings, multiSmRunData.sharedEvents, info);

            if (!ranSingleSm)
            {
                diagramRan = false;
                return;
            }

            diagramRan = true;
        }

        info.success = diagramRan;
        if (diagramRan)
        {
            info.lastCodeGenEndDateTime = DateTime.Now;
        }
    }

    private RunnerSettings BuildBaseRunnerSettings(string absolutePath)
    {
        RunnerSettings runnerSettings = new(diagramFile: absolutePath, transpilerId: _diagramOptions.Lang);
        runnerSettings.simulation.enableGeneration = !_diagramOptions.NoSimGen; // enabled by default
        runnerSettings.propagateExceptions = _runHandlerOptions.PropagateExceptions;
        runnerSettings.dumpErrorsToFile = _runHandlerOptions.DumpErrorsToFile;
        return runnerSettings;
    }

    private (int count, List<string> stateMachineNames, HashSet<string> sharedEvents) BuildMultiSmRunData(string callerFilePath, string absolutePath)
    {
        if (!DiagramFileAssociator.IsDrawIoFile(absolutePath))
        {
            return new(count: 0, stateMachineNames: new(), sharedEvents: new());
        }

        var discoveryRunner = new SmRunner(settings: BuildBaseRunnerSettings(absolutePath), renderConfig: null, callerFilePath: callerFilePath);
        var inputSmBuilder = discoveryRunner.GetExperimentalAccess().InputSmBuilder;
        inputSmBuilder.ConvertDiagramFileToSmVertices(absolutePath);

        var stateMachineNames = inputSmBuilder.GetRootVertices().OfType<StateMachine>().Select(sm => sm.Name).OrderBy(x => x).ToList();
        if (stateMachineNames.Count <= 1)
        {
            return new(count: stateMachineNames.Count, stateMachineNames: stateMachineNames, sharedEvents: new());
        }

        HashSet<string> sharedEvents = new();
        foreach (var name in stateMachineNames)
        {
            inputSmBuilder.FindStateMachineByName(name);
            inputSmBuilder.FinishRunning();
            sharedEvents.UnionWith(inputSmBuilder.GetStateMachine().GetEventSet());
        }

        return new(count: stateMachineNames.Count, stateMachineNames: stateMachineNames, sharedEvents: sharedEvents);
    }

    private bool RunSingleStateMachine(string callerFilePath, RunnerSettings runnerSettings, HashSet<string> sharedEvents, DiagramRunInfo info)
    {
        SmRunner smRunner = new(settings: runnerSettings, renderConfig: null, callerFilePath: callerFilePath);
        smRunner.GetExperimentalAccess().DiServiceProvider.AddSingletonT<ICodeFileWriter, LoggingCodeFileWriter>();

        if (smRunner.PreDiagramBasedSettingsException != null)
        {
            warningCount++;
            _runConsole.ErrorMarkupLine("\nFailed while trying to read diagram for settings.\n");
            smRunner.PrintAndThrowIfPreDiagramSettingsException();   // need to do this before we check the transpiler ID
            throw new Exception("Should not get here.");
        }

        if (runnerSettings.transpilerId == TranspilerId.NotYetSet)
        {
            _runConsole.WarnMarkupLine($"Ignoring diagram as no language specified `--lang` and no transpiler ID found in diagram.");
            warningCount++;
            return false;
        }

        LoggingCodeFileWriter loggingCodeFileWriter = (LoggingCodeFileWriter)smRunner.GetExperimentalAccess().DiServiceProvider.GetInstanceOf<ICodeFileWriter>();

        if (sharedEvents.Count > 0)
        {
            smRunner.SmTransformer.InsertAfterFirstMatch(StandardSmTransformer.TransformationId.Standard_AddUsedEventsToSm, sm =>
            {
                sm._events.AddRange(sharedEvents);
            });
        }

        try
        {
            smRunner.Run();
            return true;
        }
        finally
        {
            info.writtenFileAbsolutePaths.AddRange(loggingCodeFileWriter.filePathsWritten);
        }
    }
}
