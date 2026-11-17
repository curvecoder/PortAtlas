using FileForge.IO;
using PortAtlas.Cli.Configuration;
using PortAtlas.Cli.Dashboard;
using PortAtlas.Cli.Terminal;
using PortAtlas.Core.Deduplication;
using PortAtlas.Core.Engine;
using PortAtlas.Core.Export;
using PortAtlas.Core.Filtering;
using PortAtlas.Core.Models;
using PortAtlas.Core.Providers;
using PortAtlas.Core.Resolution;
using ProcessForge.Filtering;
using ProcessForge.Inspectors;
using SocketForge.Factories;

namespace PortAtlas.Cli.Audit;

/// <summary>
/// Runs the <c>audit</c> command: scans sockets continuously, writes deduplicated flows and drives the terminal dashboard.
/// </summary>
public static class ContinuousAuditRunner
{
    /// <summary>
    /// Runs the audit loop until the user presses Q, Ctrl+C or the process receives SIGTERM.
    /// </summary>
    /// <param name="config">Effective configuration (file values overridden by command-line options).</param>
    /// <param name="configPath">Configuration file path, updated when an exclusion is added from the dashboard.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>Exit code (0 on graceful shutdown, 1 on configuration error).</returns>
    /// <exception cref="ArgumentNullException">When an argument is null.</exception>
    public static async Task<int> RunAsync(AppConfiguration config, string configPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(configPath);

        var delimiter = !string.IsNullOrEmpty(config.CsvDelimiter) ? config.CsvDelimiter[0] : ',';
        var topologyMapper = new AuditTopologyMapper();

        IReadOnlyList<AuditCsvColumn> selectedColumns;
        try
        {
            selectedColumns = AuditCsvColumnCatalog.ResolveColumns(config.CsvColumns);
        }
        catch (ArgumentException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Column configuration error: {ex.Message}");
            Console.ResetColor();
            return 1;
        }

        // The log is read back at the next start to continue its flows, which needs these columns: a log without
        // them could never be resumed.
        var missingColumns = AuditCsvHistoryParser.ResumeRequiredColumns
            .Where(required => !selectedColumns.Any(column => column.ToString().Equals(required, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (missingColumns.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Column configuration error: the audit log must keep the {string.Join(", ", missingColumns)} column(s), which identify a flow when the log is read back at the next start.");
            Console.ResetColor();
            return 1;
        }

        var useDashboard = config.EnableDashboard && !Console.IsInputRedirected && !Console.IsOutputRedirected;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        IReadOnlyList<NetworkAuditEntry> parsedEntries;
        try
        {
            // A rewrite interrupted between its renames left the complete new log in <log>.pending: it replaces the
            // log before the log is read, and an incomplete <log>.tmp is dropped.
            AtomicFileWriter.RecoverInterruptedWrite(config.OutputCsvPath);
            if (!string.IsNullOrWhiteSpace(config.OutputNdjsonPath))
            {
                AtomicFileWriter.RecoverInterruptedWrite(config.OutputNdjsonPath);
            }

            // The whole log is read: it is rewritten from the flows held in memory, so a flow left out here would
            // disappear from it.
            parsedEntries = await new AuditCsvHistoryParser().ParseLogForResumeAsync(
                config.OutputCsvPath,
                maxRows: null,
                delimiter,
                cts.Token).ConfigureAwait(false);

            // The startup rewrite replaces the NDJSON log with the flows resumed from the CSV log: when that log
            // gave none, the records of the NDJSON log would be erased.
            await NdjsonLogLossValidator.EnsureNoFlowIsLostAsync(
                config.OutputCsvPath,
                parsedEntries.Count,
                config.OutputNdjsonPath,
                cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidDataException or AuditLogFormatException or IOException or UnauthorizedAccessException)
        {
            // Resuming such a log would drop the flows it holds at the first rewrite: the audit does not start. The
            // log is read before any resolver or dashboard exists, so nothing is left open and the message stays on
            // screen.
            WriteAuditLogError(ex);
            return 1;
        }

        var hostProvider = new LocalServerHostProvider();
        var host = hostProvider.GetCurrentHostMetadata();

        if (!useDashboard)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("  PortAtlas - Continuous Network Flow Auditor");
            Console.WriteLine("==================================================");
            Console.WriteLine($"Host server     : {host.HostName} ({host.OperatingSystemDescription})");
            Console.WriteLine($"Primary IP      : {host.PrimaryIpAddress}");
            Console.WriteLine($"CSV output      : {config.OutputCsvPath}");
            if (!string.IsNullOrWhiteSpace(config.OutputNdjsonPath))
            {
                Console.WriteLine($"NDJSON output   : {config.OutputNdjsonPath}");
            }
            if (!string.IsNullOrWhiteSpace(config.OutputHtmlMapPath))
            {
                Console.WriteLine($"HTML map        : {config.OutputHtmlMapPath}");
            }

            Console.WriteLine($"CSV columns     : {selectedColumns.Count} active columns");
            Console.WriteLine($"Reverse DNS     : {(config.ResolveRemoteHostNames ? $"Enabled (cache: {config.DnsCacheFilePath})" : "Disabled")}");
            Console.WriteLine($"IP owners (RDAP): {(config.ResolveIpOwners ? "Enabled (RDAP with circuit breaker)" : "Disabled")}");
            Console.WriteLine($"Scan interval   : every {config.ScanIntervalSeconds} second(s)");
            Console.WriteLine($"Loopback filter : {(config.ExcludeLoopbackFlows ? "Enabled" : "Disabled")}");
            if (config.ExcludedRemoteIps.Count > 0)
            {
                Console.WriteLine($"Excluded IPs    : {string.Join(", ", config.ExcludedRemoteIps)}");
            }

            Console.WriteLine("Starting continuous audit... (Press 'm' to refresh topology map, 'q' to stop)\n");
        }

        // One scanner for the whole audit: its process cache, keyed by PID and start time, is what spares
        // re-reading every process at each scan cycle.
        var socketScanner = SocketScannerFactory.Create();

        var filterOptions = new ProcessExclusionOptions
        {
            ExcludedProcessIds = config.ExcludedPids,
            ExcludedExactNames = config.ExcludedExactNames,
            ExcludedNamePrefixes = config.ExcludedPrefixes,
            ExcludedNameSuffixes = config.ExcludedSuffixes,
            ExcludedNameContains = config.ExcludedContains,
            ExcludeSystemAccounts = config.ExcludeSystemAccounts
        };

        var exclusionFilter = new ProcessExclusionFilter(filterOptions);

        var flowFilterOptions = new NetworkFlowFilterOptions
        {
            ExcludeLoopbackFlows = config.ExcludeLoopbackFlows,
            ExcludedLocalPorts = new HashSet<int>(config.ExcludedLocalPorts),
            ExcludedRemotePorts = new HashSet<int>(config.ExcludedRemotePorts),
            ExcludedRemoteIps = config.ExcludedRemoteIps
        };

        var networkFlowFilter = new NetworkFlowFilter(flowFilterOptions);

        var deduplicationRegister = new ConnectionDeduplicationRegister();

        var csvOptions = new AuditCsvOptions
        {
            SelectedColumns = selectedColumns,
            Delimiter = delimiter
        };
        var csvFormatter = new AuditCsvFormatter(csvOptions);

        var exclusionEvaluator = new DnsAddressExclusionEvaluator(config.DnsExcludedNetworks);
        var dnsCacheStore = new PersistentDnsCacheStore(config.DnsCacheFilePath);

        using var orgResolver = config.ResolveIpOwners
            ? new RdapIpOrganizationResolver()
            : (IIpOrganizationResolver)new NoOpIpOrganizationResolver();

        // A DNS cache that cannot be read stops the audit here (DnsCacheUnreadableException, shown by Program.Main),
        // before the log is rewritten or the cache saved, and before any dashboard exists, so the message stays on screen.
        IHostNameResolver hostNameResolver = config.ResolveRemoteHostNames
            ? new CachedHostNameResolver(TimeSpan.FromMilliseconds(config.DnsResolutionTimeoutMs), exclusionEvaluator, dnsCacheStore, orgResolver, config.CustomHostAliases)
            : new NoOpHostNameResolver();

        // Owns the resolver on every path out of this method (cancellation, log error, normal stop): its disposal
        // finishes the background resolutions and saves the DNS cache. It only exists once the cache was read, so the
        // refusal of an unreadable cache, thrown by the constructor above, never reaches a save that would replace it.
        await using var resolverOwner = hostNameResolver as IAsyncDisposable;

        var logRewriter = string.IsNullOrWhiteSpace(config.OutputNdjsonPath)
            ? new AuditLogRewriter(config.OutputCsvPath, csvFormatter)
            : new AuditLogRewriter(config.OutputCsvPath, csvFormatter, config.OutputNdjsonPath, new AuditNdjsonFormatter());

        var engine = new FlowAuditEngine(
            socketScanner,
            exclusionFilter,
            networkFlowFilter,
            deduplicationRegister,
            hostProvider,
            logRewriter,
            hostNameResolver,
            TimeSpan.FromSeconds(config.ScanIntervalSeconds),
            TimeSpan.FromMinutes(config.LogRewriteIntervalMinutes));

        var exclusionConfigurator = new FlowExclusionConfigurator(config, configPath, networkFlowFilter, exclusionFilter);

        // A name already in the DNS cache raises no resolution event during the audit, so a flow logged before
        // its name was known would stay unnamed on the dashboard: the names are taken from the cache here.
        var historicalEntries = new ReloadedFlowNameResolver(hostNameResolver).ResolveFromCache(parsedEntries);
        deduplicationRegister.Hydrate(historicalEntries);

        try
        {
            // Written once at start so that a log that cannot be replaced (held open, read-only folder) stops the
            // audit now, with the message on screen, rather than at the first rewrite behind the dashboard.
            await engine.RewriteLogAsync(cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            WriteAuditLogError(ex);
            return 1;
        }

        FlowAuditDashboardRenderer? dashboard = null;
        if (useDashboard)
        {
            dashboard = new FlowAuditDashboardRenderer(
                config.ScanIntervalSeconds,
                config.OutputCsvPath,
                networkFlowFilter,
                exclusionFilter,
                hostProvider,
                config.MaskServerName,
                config.MaskLocalIp);
            dashboard.ClearScreen();
            engine.ScanCycleCompleted += (_, e) => dashboard.Render(e.Result, hostProvider.GetCurrentHostMetadata());
            hostNameResolver.HostNameResolved += (_, e) => dashboard.UpdateRemoteHostName(e.Address, e.HostName);
            if (historicalEntries.Count > 0)
            {
                dashboard.HydrateDiscoveries(historicalEntries);
            }
        }

        engine.ScanCycleFailed += (_, e) => ReportScanFailure(dashboard, e);
        engine.LogRewriteFailed += (_, e) => ReportWarning(dashboard, FormatLogRewriteFailure(e.Error));

        using var mapRefreshScheduler = new TopologyMapRefreshScheduler(
            ex => ReportWarning(dashboard, $"Topology map not written: {ex.Message}"));
        if (!string.IsNullOrWhiteSpace(config.OutputHtmlMapPath))
        {
            engine.ScanCycleCompleted += (_, _) => mapRefreshScheduler.RequestRefresh(
                cancellationToken => topologyMapper.GenerateHtmlMapFromEntriesAsync(
                    deduplicationRegister.GetAllEntries(),
                    config.OutputHtmlMapPath,
                    TopologyMapConfigurationBuilder.BuildFilterOptions(config),
                    TopologyMapConfigurationBuilder.BuildRenderingOptions(config),
                    $"Network Topology Map - {host.HostName}",
                    cancellationToken),
                cts.Token);
        }

        using var terminationSignals = new TerminationSignalListener(cts);

        var auditTask = engine.RunContinuousAuditAsync(cts.Token);

        if (!Console.IsInputRedirected)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!cts.IsCancellationRequested)
                    {
                        dashboard?.CheckAndRedrawIfWindowChanged();

                        while (Console.KeyAvailable)
                        {
                            var key = Console.ReadKey(intercept: true);
                            if (key.KeyChar is 'q' or 'Q')
                            {
                                await cts.CancelAsync().ConfigureAwait(false);
                                return;
                            }

                            if (key.KeyChar is 'c' or 'C')
                            {
                                dashboard?.ForceRedraw();
                            }
                            else if (key.KeyChar is 'v' or 'V' or 'f' or 'F')
                            {
                                dashboard?.ToggleViewMode();
                            }
                            else if (key.KeyChar is 'm' or 'M')
                            {
                                await mapRefreshScheduler.GenerateExclusivelyAsync(
                                    cancellationToken => WriteRequestedMapsAsync(
                                        topologyMapper,
                                        deduplicationRegister.GetAllEntries(),
                                        config,
                                        host.HostName,
                                        cancellationToken),
                                    cts.Token).ConfigureAwait(false);
                            }
                            else if (dashboard is not null)
                            {
                                switch (key.Key)
                                {
                                    case ConsoleKey.UpArrow:
                                        dashboard.MoveSelectionUp();
                                        break;

                                    case ConsoleKey.DownArrow:
                                        dashboard.MoveSelectionDown();
                                        break;

                                    case ConsoleKey.LeftArrow:
                                        dashboard.MoveSelectionLeft();
                                        break;

                                    case ConsoleKey.RightArrow:
                                        dashboard.MoveSelectionRight();
                                        break;

                                    case ConsoleKey.PageDown:
                                        dashboard.NextPage();
                                        break;

                                    case ConsoleKey.PageUp:
                                        dashboard.PreviousPage();
                                        break;

                                    case ConsoleKey.Enter:
                                        dashboard.ApplyExclusionAtSelection(exclusionConfigurator);
                                        break;

                                    case ConsoleKey.Escape:
                                        dashboard.CancelSelection();
                                        break;

                                    default:
                                        if (key.KeyChar is 'x' or 'X' or 'e' or 'E')
                                        {
                                            dashboard.ApplyExclusionAtSelection(exclusionConfigurator);
                                        }
                                        else if (key.KeyChar is 'n' or 'N')
                                        {
                                            dashboard.NextPage();
                                        }
                                        else if (key.KeyChar is 'p' or 'P')
                                        {
                                            dashboard.PreviousPage();
                                        }
                                        else if (key.KeyChar is 'l' or 'L' or '+')
                                        {
                                            dashboard.LoadNextBatch();
                                        }
                                        else if (key.KeyChar is 'a' or 'A')
                                        {
                                            dashboard.ShowAllDiscoveries();
                                        }

                                        break;
                                }
                            }
                        }

                        await Task.Delay(20, cts.Token).ConfigureAwait(false);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Console.KeyAvailable throws when stdin is not a console (service host, piped input). The keyboard
                    // loop stops; the audit keeps running and is stopped by Ctrl+C or SIGTERM instead of the Q key.
                }
                catch (OperationCanceledException)
                {
                    // The audit is stopping (Q, Ctrl+C or SIGTERM): the keyboard loop ends with it, and the main task
                    // observes the same cancellation to flush the writers.
                }
            }, cts.Token);
        }

        var exitCode = 0;
        try
        {
            await auditTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            dashboard?.RestoreCursor();
            if (!useDashboard)
            {
                Console.WriteLine("Graceful shutdown of network flow audit.");
            }
        }
        catch (ConsecutiveScanFailuresException ex)
        {
            dashboard?.RestoreCursor();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(ex.Message);
            Console.ResetColor();
            exitCode = 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The logs are written a last time when the audit stops; when that fails, the operator must know that
            // the observations since the last rewrite are not on disk.
            dashboard?.RestoreCursor();
            WriteAuditLogError(ex);
            exitCode = 1;
        }
        finally
        {
            dashboard?.RestoreCursor();
            // An audit stopped by repeated scan failures or a log error is not cancelled yet: cancelling stops the
            // keyboard loop, so no M key generation starts once the scheduler is disposed. The wait comes last so
            // that an unexpected map failure, rethrown here, does not skip the cleanup above.
            await cts.CancelAsync().ConfigureAwait(false);
            await mapRefreshScheduler.WaitForRefreshesAsync().ConfigureAwait(false);
        }

        return exitCode;
    }

    private static void ReportScanFailure(FlowAuditDashboardRenderer? dashboard, FlowAuditCycleFailedEventArgs failure) =>
        ReportWarning(
            dashboard,
            $"Scan failed ({failure.ConsecutiveFailures} of {failure.ConsecutiveFailureLimit} in a row): {failure.Error.Message}");

    // The message of the exception names the log that was not replaced.
    private static string FormatLogRewriteFailure(Exception error) =>
        $"Could not rewrite the audit log: {error.Message.TrimEnd('.')}. The flows stay in memory; the next rewrite retries.";

    private static async Task WriteRequestedMapsAsync(
        AuditTopologyMapper topologyMapper,
        IEnumerable<NetworkAuditEntry> entries,
        AppConfiguration config,
        string hostName,
        CancellationToken cancellationToken)
    {
        var targetHtml = string.IsNullOrWhiteSpace(config.OutputHtmlMapPath)
            ? DefaultOutputFileNames.TopologyHtml
            : config.OutputHtmlMapPath;
        var graph = await topologyMapper.GenerateHtmlMapFromEntriesAsync(
            entries,
            targetHtml,
            TopologyMapConfigurationBuilder.BuildFilterOptions(config),
            TopologyMapConfigurationBuilder.BuildRenderingOptions(config),
            $"Network Topology Map - {hostName}",
            cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(config.OutputDrawioPath))
        {
            return;
        }

        await AuditTopologyMapper.ExportToDrawioAsync(
            graph,
            config.OutputDrawioPath,
            $"PortAtlas Network Topology - {hostName}",
            TopologyMapConfigurationBuilder.BuildDrawioEdgeMode(config),
            TopologyMapConfigurationBuilder.BuildDrawioTheme(config),
            TopologyMapConfigurationBuilder.BuildDrawioLayoutOptions(config),
            cancellationToken).ConfigureAwait(false);
    }

    private static void ReportWarning(FlowAuditDashboardRenderer? dashboard, string message)
    {
        if (dashboard is not null)
        {
            dashboard.ShowWarning(message);
            return;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] {message}");
        Console.ResetColor();
    }

    private static void WriteAuditLogError(Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Audit log error: {ex.Message}");
        Console.ResetColor();
    }
}
