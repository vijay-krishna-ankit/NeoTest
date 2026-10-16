using System;
using System.ComponentModel.Design;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using EnvDTE;
using Task = System.Threading.Tasks.Task;

namespace CodeCoverage
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideOptionPage(typeof(CoverageOptionsPage), "Neo-Test", "General", 0, 0, true)]
    [ProvideToolWindow(typeof(CoverageDetailsToolWindow))]
    [ProvideToolWindow(typeof(CoverageSettingsToolWindow))]
    [ProvideAutoLoad(Microsoft.VisualStudio.Shell.Interop.UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
    [System.Runtime.InteropServices.Guid(PackageGuids.PackageString)]
    public sealed class CodeCoveragePackage : AsyncPackage
    {
        private CoverageReportWatcher reportWatcher;
        private int testCommandIsRunning;
        private int testCommandWasCancelled;
        private readonly object testProcessLock = new object();
        private System.Diagnostics.Process activeTestProcess;
        private DTE dte;
        private SolutionEvents solutionEvents;
        private string watchedResultsDirectory;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var options = (CoverageOptionsPage)GetDialogPage(typeof(CoverageOptionsPage));
            CoverageStyle.SetOptions(options.CoveredLineColor, options.UncoveredLineColor, options.FailedTestLineColor,
                options.GoodCoverageThreshold, options.WarningCoverageThreshold, options.AlwaysIncludeProjects, options.AlwaysExcludeProjects);
            CoverageStyle.TestCommand = options.TestCommand ?? string.Empty;
            CoverageStyle.CurrentFileTestCommand = options.CurrentFileTestCommand ?? string.Empty;
            CoverageStyle.DiffBaseReference = string.IsNullOrWhiteSpace(options.DiffBaseReference) ? "HEAD" : options.DiffBaseReference.Trim();
            CoverageStyle.MinimumSolutionLineCoverage = Math.Max(0, Math.Min(100, options.MinimumSolutionLineCoverage));
            CoverageStyle.MinimumSolutionBranchCoverage = Math.Max(0, Math.Min(100, options.MinimumSolutionBranchCoverage));
            CoverageStyle.TestCommandTimeoutSeconds = Math.Max(10, Math.Min(3600, options.TestCommandTimeoutSeconds));
            CoverageStyle.ResultsDirectory = NormalizeResultsDirectory(options.ResultsDirectory);
            CoverageStyle.ParallelTestResultWindowSeconds = Math.Max(1, Math.Min(120, options.ParallelTestResultWindowSeconds));
            CoverageStyle.RetainMissingResultsSeconds = Math.Max(0, Math.Min(300, options.RetainMissingResultsSeconds));
            CoverageStyle.ExcludeGeneratedFiles = options.ExcludeGeneratedFiles;
            CoverageStyle.GeneratedFilePatterns = options.GeneratedFilePatterns ?? string.Empty;
            CoverageStyle.NotifyOnCoverageCategoryDrop = options.NotifyOnCoverageCategoryDrop;
            CoverageStyle.ProjectCoverageThresholds = options.ProjectCoverageThresholds ?? string.Empty;
            CoveragePathMapper.Configure(options.ReportPathMappings);
            CoverageSettingsHost.Read = options.CreateSnapshot;
            CoverageSettingsHost.Apply = options.ApplySnapshot;
            var commandService = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            commandService?.AddCommand(new MenuCommand(
                (_, __) => CoverageStore.Clear(),
                new CommandID(PackageGuids.CommandSet, 0x0100)));
            commandService?.AddCommand(new MenuCommand((_, __) => OpenCoverageDetails(),
                new CommandID(PackageGuids.CommandSet, 0x0101)));
            commandService?.AddCommand(new MenuCommand((_, __) => RunTestsWithCoverage(),
                new CommandID(PackageGuids.CommandSet, 0x0102)));
            commandService?.AddCommand(new MenuCommand((_, __) => CoverageNavigator.MoveToNext(CoverageLineState.Uncovered),
                new CommandID(PackageGuids.CommandSet, 0x0103)));
            commandService?.AddCommand(new MenuCommand((_, __) => CoverageNavigator.MoveToNext(CoverageLineState.Failed),
                new CommandID(PackageGuids.CommandSet, 0x0104)));
            commandService?.AddCommand(new MenuCommand((_, __) => CoverageExporter.Export(),
                new CommandID(PackageGuids.CommandSet, 0x0105)));
            commandService?.AddCommand(new MenuCommand((_, __) => CancelTestCommand(),
                new CommandID(PackageGuids.CommandSet, 0x0106)));
            commandService?.AddCommand(new MenuCommand((_, __) => RunTestsForCurrentFile(),
                new CommandID(PackageGuids.CommandSet, 0x0107)));
            commandService?.AddCommand(new MenuCommand((_, __) => RefreshCoverage(),
                new CommandID(PackageGuids.CommandSet, 0x0108)));
            commandService?.AddCommand(new MenuCommand((_, __) => OpenSettings(),
                new CommandID(PackageGuids.CommandSet, 0x0109)));

            dte = await GetServiceAsync(typeof(DTE)) as DTE;
            if (dte != null)
            {
                solutionEvents = dte.Events.SolutionEvents;
                solutionEvents.Opened += OnSolutionOpened;
                solutionEvents.AfterClosing += OnSolutionAfterClosing;
                CoverageStyle.Changed += OnCoverageStyleChanged;
                CoverageStore.CoverageCategoryDropped += OnCoverageCategoryDropped;
                CoverageStore.RefreshRequested += OnRefreshRequested;
                RecreateWatcher();
            }
        }

        protected override void Dispose(bool disposing)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (disposing)
            {
                CoverageStyle.Changed -= OnCoverageStyleChanged;
                CoverageStore.CoverageCategoryDropped -= OnCoverageCategoryDropped;
                CoverageStore.RefreshRequested -= OnRefreshRequested;
                if (solutionEvents != null)
                {
                    solutionEvents.Opened -= OnSolutionOpened;
                    solutionEvents.AfterClosing -= OnSolutionAfterClosing;
                }
                reportWatcher?.Dispose();
                CancelTestCommand();
            }
            base.Dispose(disposing);
        }

        private void OnSolutionOpened()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            RecreateWatcher();
        }

        private void OnSolutionAfterClosing()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            reportWatcher?.Dispose();
            reportWatcher = null;
            watchedResultsDirectory = null;
            CoverageStore.Clear();
            CoverageStore.SetStatus("Open a solution to watch for coverage reports.");
        }

        private void OnCoverageStyleChanged(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var configuredDirectory = NormalizeResultsDirectory(CoverageStyle.ResultsDirectory);
            if (!string.Equals(configuredDirectory, watchedResultsDirectory, StringComparison.OrdinalIgnoreCase)) RecreateWatcher();
            else reportWatcher?.ConfigurationChanged();
        }

        private void RecreateWatcher()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var solutionPath = dte?.Solution?.FullName;
            if (string.IsNullOrWhiteSpace(solutionPath)) return;
            var configuredDirectory = NormalizeResultsDirectory(CoverageStyle.ResultsDirectory);
            reportWatcher?.Dispose();
            reportWatcher = new CoverageReportWatcher(System.IO.Path.GetDirectoryName(solutionPath), configuredDirectory);
            watchedResultsDirectory = configuredDirectory;
        }

        private void RunTestsForCurrentFile()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var path = dte?.ActiveDocument?.FullName;
            if (string.IsNullOrWhiteSpace(path))
            {
                CoverageStore.SetStatus("Open a source file before running tests for the current file.");
                return;
            }
            if (string.IsNullOrWhiteSpace(CoverageStyle.CurrentFileTestCommand))
            {
                CoverageStore.SetStatus("Set Current file test command in Tools → Neo-Test → General. Use {file} and {name} placeholders.");
                return;
            }
            var fileArgument = "\"" + path.Replace("\"", "\"\"") + "\"";
            var command = CoverageStyle.CurrentFileTestCommand
                .Replace("{file}", fileArgument)
                .Replace("{name}", System.IO.Path.GetFileName(path));
            RunTestsWithCoverage(command);
        }

        private void RunTestsWithCoverage(string command = null)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var testCommand = command ?? CoverageStyle.TestCommand;
            if (string.IsNullOrWhiteSpace(testCommand))
            {
                CoverageStore.SetStatus("Set Test command in Tools → Neo-Test → General before running tests with coverage.");
                return;
            }

            var dte = GetService(typeof(DTE)) as DTE;
            if (dte == null)
            {
                CoverageStore.SetStatus("Visual Studio's automation service is unavailable, so the test command cannot be run.");
                return;
            }
            var solutionPath = dte.Solution?.FullName;
            var solutionDirectory = string.IsNullOrWhiteSpace(solutionPath)
                ? null : System.IO.Path.GetDirectoryName(solutionPath);
            if (string.IsNullOrWhiteSpace(solutionDirectory))
            {
                CoverageStore.SetStatus("Open a solution before running tests with coverage.");
                return;
            }

            if (Interlocked.CompareExchange(ref testCommandIsRunning, 1, 0) != 0)
            {
                CoverageStore.SetStatus("The configured test command is already running.");
                return;
            }

            CoverageStore.SetStatus("Running configured test command…");
            var startInfo = new ProcessStartInfo("cmd.exe", "/d /s /c \"" + testCommand + "\"")
            {
                WorkingDirectory = solutionDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            System.Diagnostics.Process process;
            try { process = System.Diagnostics.Process.Start(startInfo); }
            catch (Exception exception)
            {
                Interlocked.Exchange(ref testCommandIsRunning, 0);
                CoverageStore.SetStatus("The configured test command could not be started: " + exception.Message);
                return;
            }
            if (process == null)
            {
                Interlocked.Exchange(ref testCommandIsRunning, 0);
                CoverageStore.SetStatus("The configured test command could not be started.");
                return;
            }
            var outputTail = new Queue<string>();
            var outputLock = new object();
            DataReceivedEventHandler recordOutput = (_, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Data)) return;
                lock (outputLock)
                {
                    outputTail.Enqueue(args.Data.Trim());
                    while (outputTail.Count > 8) outputTail.Dequeue();
                }
            };
            process.OutputDataReceived += recordOutput;
            process.ErrorDataReceived += recordOutput;
            lock (testProcessLock)
            {
                activeTestProcess = process;
                Interlocked.Exchange(ref testCommandWasCancelled, 0);
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _ = Task.Run(() =>
            {
                try
                {
                    using (process)
                    {
                        if (!process.WaitForExit(CoverageStyle.TestCommandTimeoutSeconds * 1000))
                        {
                            TryStopProcess(process);
                            process.WaitForExit(5000);
                            CoverageStore.SetStatus("The configured test command timed out after " + CoverageStyle.TestCommandTimeoutSeconds + " seconds and was stopped.");
                        }
                        else if (Interlocked.CompareExchange(ref testCommandWasCancelled, 0, 0) != 0)
                        {
                            CoverageStore.SetStatus("The configured test command was cancelled.");
                        }
                        else
                        {
                            CoverageStore.SetStatus(process.ExitCode == 0
                                ? "Test command completed. Waiting for coverage reports…"
                                : "Test command exited with code " + process.ExitCode + "." + FormatOutputTail(outputTail, outputLock));
                        }
                    }
                }
                catch (Exception exception)
                {
                    CoverageStore.SetStatus("The configured test command ended unexpectedly: " + exception.Message);
                }
                finally
                {
                    lock (testProcessLock)
                    {
                        if (ReferenceEquals(activeTestProcess, process)) activeTestProcess = null;
                    }
                    Interlocked.Exchange(ref testCommandIsRunning, 0);
                }
            });
        }

        private void CancelTestCommand()
        {
            System.Diagnostics.Process process;
            lock (testProcessLock)
            {
                process = activeTestProcess;
                if (process != null) Interlocked.Exchange(ref testCommandWasCancelled, 1);
            }
            if (process == null)
            {
                CoverageStore.SetStatus("No configured test command is running.");
                return;
            }
            TryStopProcess(process);
            CoverageStore.SetStatus("Cancelling the configured test command…");
        }

        private void RefreshCoverage()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (reportWatcher == null)
            {
                CoverageStore.SetStatus("Open a solution before refreshing coverage reports.");
                return;
            }
            reportWatcher.RefreshNow();
        }

        private void OnRefreshRequested(object sender, EventArgs e)
        {
            JoinableTaskFactory.RunAsync(async () =>
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync();
                RefreshCoverage();
            }).FileAndForget("CodeCoverage/RefreshRequested");
        }

        private void OnCoverageCategoryDropped(object sender, CoverageCategoryChangedEventArgs e)
        {
            if (!CoverageStyle.NotifyOnCoverageCategoryDrop || e.Paths.Count == 0) return;
            JoinableTaskFactory.RunAsync(async () =>
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync();
                if (DisposalToken.IsCancellationRequested) return;
                var fileNames = string.Join(", ", e.Paths.Take(3).Select(System.IO.Path.GetFileName));
                var suffix = e.Paths.Count > 3 ? " and " + (e.Paths.Count - 3) + " more" : string.Empty;
                VsShellUtilities.ShowMessageBox(this, "Coverage dropped to a lower category for " + fileNames + suffix + ".",
                    "Neo-Test", OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }).FileAndForget("CodeCoverage/CoverageCategoryDrop");
        }

        private static void TryStopProcess(System.Diagnostics.Process process)
        {
            try
            {
                if (process.HasExited) return;
                // The configured command is launched through cmd.exe. taskkill's /T switch terminates
                // the complete child-process tree, including dotnet/vstest hosts that outlive cmd.exe.
                using (var taskKill = System.Diagnostics.Process.Start(new ProcessStartInfo("taskkill.exe", "/PID " + process.Id + " /T /F")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                }))
                {
                    taskKill?.WaitForExit(5000);
                }
                if (!process.HasExited) process.Kill();
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        private static string FormatOutputTail(Queue<string> outputTail, object outputLock)
        {
            lock (outputLock)
            {
                return outputTail.Count == 0 ? " Check the test output for details." : " Last output: " + outputTail.Last();
            }
        }

        private static string NormalizeResultsDirectory(string value)
        {
            var directory = string.IsNullOrWhiteSpace(value) ? "TestResults" : value.Trim();
            if (directory == "." || System.IO.Path.IsPathRooted(directory)) return "TestResults";
            try
            {
                var segments = directory.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                return segments.Any(segment => segment == "." || segment == "..") ? "TestResults" : directory;
            }
            catch (ArgumentException) { return "TestResults"; }
            catch (NotSupportedException) { return "TestResults"; }
        }

        private void OpenCoverageDetails()
        {
            JoinableTaskFactory.RunAsync(async () =>
            {
                var window = await ShowToolWindowAsync(typeof(CoverageDetailsToolWindow), 0, true, DisposalToken);
                await JoinableTaskFactory.SwitchToMainThreadAsync();
                var frame = window?.Frame as IVsWindowFrame;
                frame?.Show();
            }).FileAndForget("CodeCoverage/OpenCoverageDetails");
        }

        private void OpenSettings()
        {
            JoinableTaskFactory.RunAsync(async () =>
            {
                var window = await ShowToolWindowAsync(typeof(CoverageSettingsToolWindow), 0, true, DisposalToken);
                await JoinableTaskFactory.SwitchToMainThreadAsync();
                (window?.Frame as IVsWindowFrame)?.Show();
            }).FileAndForget("CodeCoverage/OpenSettings");
        }
    }
}
