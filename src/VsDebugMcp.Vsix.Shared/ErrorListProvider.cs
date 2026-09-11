using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.TableControl;
using Microsoft.VisualStudio.Shell.TableManager;
using VsDebugMcp.Protocol;

namespace VsDebugMcp_Vsix;

internal sealed class ErrorListProvider
{
    private const int DefaultMaxCount = 200;
    private const int MaximumMaxCount = 1000;
    private const int MaximumBuildTaskIdLength = 256;
    private const int MaximumProjectLength = 1024;
    private const int MaximumFileLength = 32768;
    private static readonly TimeSpan SnapshotQuietPeriod = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan SnapshotWaitTimeout = TimeSpan.FromSeconds(2);
    private readonly AsyncPackage _package;
    private readonly string _vsInstanceId;

    public ErrorListProvider(AsyncPackage package, string vsInstanceId)
    {
        _package = package;
        _vsInstanceId = vsInstanceId;
    }

    public async Task<GetErrorsResponse> GetErrorsAsync(
        GetErrorsRequest request,
        CancellationToken cancellationToken)
    {
        var filters = ValidateRequest(request);
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var diagnostics = new List<VisualStudioDiagnostic>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Track 1: Query ErrorList TableControl entries directly
        try
        {
            var componentModel = await _package.GetServiceAsync(typeof(SComponentModel)) as IComponentModel;
            var errorList = componentModel?.GetService<IErrorList>();
            if (errorList?.TableControl != null)
            {
                var tableControl = errorList.TableControl;
                await tableControl.ForceUpdateAsync();
                foreach (var handle in tableControl.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ITableEntry? entry = null;
                    if (handle.TryGetEntry(out var directEntry))
                    {
                        entry = directEntry;
                    }
                    else if (handle.TryGetSnapshot(out var snapshot, out var index))
                    {
                        snapshot.StartCaching();
                        try
                        {
                            entry = new SnapshotEntry(snapshot, index);
                        }
                        finally
                        {
                            snapshot.StopCaching();
                        }
                    }

                    if (entry != null && TryReadDiagnostic(entry, out var diag) && Matches(diag, filters))
                    {
                        var key = $"{diag.FilePath}:{diag.Line}:{diag.Column}:{diag.Code}:{diag.Message}";
                        if (seenKeys.Add(key))
                        {
                            diagnostics.Add(diag);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // TableControl read failed or incomplete, continue to Track 2 fallback
        }

        // Track 2: If diagnostics is empty, read and parse Build Output logs
        if (diagnostics.Count == 0)
        {
            try
            {
                var dte = await _package.GetServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE80.DTE2;
                if (dte != null)
                {
                    var buildOutput = OutputWindowProvider.ReadPaneOutput(dte, "build");
                    if (!string.IsNullOrWhiteSpace(buildOutput))
                    {
                        var parsedDiagnostics = ParseBuildOutput(buildOutput, filters);
                        foreach (var diag in parsedDiagnostics)
                        {
                            var key = $"{diag.FilePath}:{diag.Line}:{diag.Column}:{diag.Code}:{diag.Message}";
                            if (seenKeys.Add(key))
                            {
                                diagnostics.Add(diag);
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Fallback parsing failed; continue to return whatever we have
            }
        }

        var returned = diagnostics.Take(filters.MaxCount).ToList();
        return new GetErrorsResponse
        {
            VsInstanceId = _vsInstanceId,
            BuildTaskId = request.BuildTaskId,
            SnapshotAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            TotalCount = diagnostics.Count,
            ReturnedCount = returned.Count,
            Truncated = returned.Count < diagnostics.Count,
            Items = returned
        };
    }

    private static ErrorFilters ValidateRequest(GetErrorsRequest request)
    {
        if (request.BuildTaskId?.Length > MaximumBuildTaskIdLength ||
            request.Project?.Length > MaximumProjectLength ||
            request.File?.Length > MaximumFileLength)
        {
            throw DiagnosticsProviderException.InvalidRequest();
        }

        var maxCount = request.MaxCount ?? DefaultMaxCount;
        if (maxCount < 1 || maxCount > MaximumMaxCount)
        {
            throw DiagnosticsProviderException.InvalidRequest();
        }

        var severities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (request.Severities is null || request.Severities.Count == 0)
        {
            severities.Add("error");
            severities.Add("warning");
        }
        else
        {
            foreach (var severity in request.Severities)
            {
                var normalized = severity?.Trim().ToLowerInvariant();
                if (normalized is not ("error" or "warning" or "message"))
                {
                    throw DiagnosticsProviderException.InvalidRequest();
                }

                severities.Add(normalized);
            }
        }

        return new ErrorFilters(
            severities,
            NormalizeOptional(request.Project),
            NormalizePath(request.File),
            maxCount);
    }

    private static readonly Regex MsvcOrClangRegex = new(
        @"^(?:\d+>\s*)?(?<file>[a-zA-Z]:[\\/][^:(]+|\S[^:(]+)\((?<line>\d+)(?:,(?<col>\d+))?(?:,\d+,\d+)?\)\s*:\s*(?<severity>fatal error|error|warning)\s+(?<code>[A-Za-z0-9_]+)\s*:\s*(?<msg>.*?)(?:\s*\[(?<proj>[^\]]+)\])?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static readonly Regex MsBuildGeneralRegex = new(
        @"^(?:\d+>\s*)?(?<tool>[A-Za-z0-9_.\-]+)\s*:\s*(?<severity>error|warning)\s+(?<code>[A-Za-z0-9_]+)\s*:\s*(?<msg>.*?)(?:\s*\[(?<proj>[^\]]+)\])?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static List<VisualStudioDiagnostic> ParseBuildOutput(string text, ErrorFilters filters)
    {
        var result = new List<VisualStudioDiagnostic>();
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            var match = MsvcOrClangRegex.Match(trimmed);
            if (match.Success)
            {
                var sevRaw = match.Groups["severity"].Value.ToLowerInvariant();
                var severity = sevRaw.Contains("error") ? "error" : "warning";
                var diag = new VisualStudioDiagnostic
                {
                    Severity = severity,
                    Code = match.Groups["code"].Value.Trim(),
                    Message = match.Groups["msg"].Value.Trim(),
                    FilePath = match.Groups["file"].Value.Trim(),
                    Line = int.TryParse(match.Groups["line"].Value, out var l) ? l : null,
                    Column = int.TryParse(match.Groups["col"].Value, out var c) ? c : null,
                    Project = match.Groups["proj"].Success ? Path.GetFileNameWithoutExtension(match.Groups["proj"].Value.Trim()) : string.Empty,
                    BuildTool = "MSBuild"
                };

                if (Matches(diag, filters))
                {
                    result.Add(diag);
                }
                continue;
            }

            var generalMatch = MsBuildGeneralRegex.Match(trimmed);
            if (generalMatch.Success)
            {
                var sevRaw = generalMatch.Groups["severity"].Value.ToLowerInvariant();
                var severity = sevRaw.Contains("error") ? "error" : "warning";
                var diag = new VisualStudioDiagnostic
                {
                    Severity = severity,
                    Code = generalMatch.Groups["code"].Value.Trim(),
                    Message = generalMatch.Groups["msg"].Value.Trim(),
                    FilePath = string.Empty,
                    Line = null,
                    Column = null,
                    Project = generalMatch.Groups["proj"].Success ? Path.GetFileNameWithoutExtension(generalMatch.Groups["proj"].Value.Trim()) : string.Empty,
                    BuildTool = generalMatch.Groups["tool"].Value.Trim()
                };

                if (Matches(diag, filters))
                {
                    result.Add(diag);
                }
            }
        }

        return result;
    }

    private static bool TryReadDiagnostic(ITableEntry entry, out VisualStudioDiagnostic diagnostic)
    {
        diagnostic = new VisualStudioDiagnostic();
        var severity = ReadSeverity(entry);
        if (string.IsNullOrEmpty(severity))
        {
            return false;
        }

        diagnostic = new VisualStudioDiagnostic
        {
            Severity = severity,
            Code = ReadString(entry, StandardTableKeyNames.ErrorCode),
            Message = ReadString(entry, StandardTableKeyNames.FullText, StandardTableKeyNames.Text),
            Project = ReadString(entry, StandardTableKeyNames.ProjectName),
            FilePath = ReadString(entry, StandardTableKeyNames.DocumentName),
            Line = ReadPosition(entry, StandardTableKeyNames.Line),
            Column = ReadPosition(entry, StandardTableKeyNames.Column),
            BuildTool = ReadString(entry, StandardTableKeyNames.BuildTool)
        };
        return true;
    }

    private static bool TryReadBuildDiagnostic(ITableEntry entry, out VisualStudioDiagnostic diagnostic)
    {
        diagnostic = new VisualStudioDiagnostic();
        if (!entry.TryGetValue(StandardTableKeyNames.ErrorSource, out var sourceValue) ||
            !TryReadErrorSource(sourceValue, out var source) ||
            (source & ErrorSource.Build) == 0)
        {
            return false;
        }

        var severity = ReadSeverity(entry);
        if (severity.Length == 0)
        {
            return false;
        }

        diagnostic = new VisualStudioDiagnostic
        {
            Severity = severity,
            Code = ReadString(entry, StandardTableKeyNames.ErrorCode),
            Message = ReadString(entry, StandardTableKeyNames.FullText, StandardTableKeyNames.Text),
            Project = ReadString(entry, StandardTableKeyNames.ProjectName),
            FilePath = ReadString(entry, StandardTableKeyNames.DocumentName),
            Line = ReadPosition(entry, StandardTableKeyNames.Line),
            Column = ReadPosition(entry, StandardTableKeyNames.Column),
            BuildTool = ReadString(entry, StandardTableKeyNames.BuildTool)
        };
        return true;
    }

    private static bool HasVisibleBuildEntries(IEnumerable<ITableEntryHandle> handles)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        foreach (var handle in handles)
        {
            if (handle.TryGetEntry(out var entry) && HasBuildSource(entry))
            {
                return true;
            }

            if (!handle.TryGetSnapshot(out var snapshot, out var index))
            {
                continue;
            }

            snapshot.StartCaching();
            try
            {
                if (HasBuildSource(new SnapshotEntry(snapshot, index)))
                {
                    return true;
                }
            }
            finally
            {
                snapshot.StopCaching();
            }
        }

        return false;
    }

    private static bool HasBuildSource(ITableEntry entry) =>
        entry.TryGetValue(StandardTableKeyNames.ErrorSource, out var sourceValue) &&
        TryReadErrorSource(sourceValue, out var source) &&
        (source & ErrorSource.Build) != 0;

    private static bool TryReadErrorSource(object? value, out ErrorSource source)
    {
        if (value is ErrorSource typed)
        {
            source = typed;
            return true;
        }

        if (value is string text && Enum.TryParse(text, true, out source))
        {
            return true;
        }

        try
        {
            source = (ErrorSource)Convert.ToInt32(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            source = default;
            return false;
        }
    }

    private static string ReadSeverity(ITableEntry entry)
    {
        if (!entry.TryGetValue(StandardTableKeyNames.ErrorSeverity, out var value) || value is null)
        {
            return string.Empty;
        }

        if (value is __VSERRORCATEGORY category)
        {
            return MapSeverity(category);
        }

        try
        {
            return MapSeverity((__VSERRORCATEGORY)Convert.ToInt32(value, CultureInfo.InvariantCulture));
        }
        catch
        {
            var text = value.ToString()?.Trim();
            if (text?.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "error";
            }

            if (text?.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "warning";
            }

            return text?.IndexOf("message", StringComparison.OrdinalIgnoreCase) >= 0 ? "message" : string.Empty;
        }
    }

    private static string MapSeverity(__VSERRORCATEGORY category) => category switch
    {
        __VSERRORCATEGORY.EC_ERROR => "error",
        __VSERRORCATEGORY.EC_WARNING => "warning",
        __VSERRORCATEGORY.EC_MESSAGE => "message",
        _ => string.Empty
    };

    private static string ReadString(ITableEntry entry, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (entry.TryGetValue(key, out var value) && value is not null)
            {
                return value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static int? ReadPosition(ITableEntry entry, string key)
    {
        if (!entry.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        try
        {
            var zeroBased = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            return zeroBased >= 0 && zeroBased < int.MaxValue ? zeroBased + 1 : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool Matches(VisualStudioDiagnostic diagnostic, ErrorFilters filters)
    {
        if (!filters.Severities.Contains(diagnostic.Severity))
        {
            return false;
        }

        if (filters.Project is not null &&
            !string.Equals(diagnostic.Project, filters.Project, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return filters.File is null || PathMatches(diagnostic.FilePath, filters.File);
    }

    private static bool PathMatches(string candidate, string filter)
    {
        var normalizedCandidate = NormalizePath(candidate);
        if (normalizedCandidate is null)
        {
            return false;
        }

        if (string.Equals(normalizedCandidate, filter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalizedCandidate.Length > filter.Length &&
            normalizedCandidate.EndsWith(filter, StringComparison.OrdinalIgnoreCase) &&
            normalizedCandidate[normalizedCandidate.Length - filter.Length - 1] == Path.DirectorySeparatorChar;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static string? NormalizePath(string? value)
    {
        var normalized = NormalizeOptional(value);
        return normalized?.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar);
    }

    private sealed class SnapshotSink : ITableDataSink
    {
        private readonly object _sync = new();
        private readonly List<object> _items = new();
        private bool _isStable;
        private long _lastChangeTicks;
        private int _hasSignal;

        public bool HasSignal => Volatile.Read(ref _hasSignal) != 0;

        public bool IsStable
        {
            get => Volatile.Read(ref _isStable);
            set
            {
                Volatile.Write(ref _isStable, value);
                MarkChanged();
            }
        }

        public void AddEntries(IReadOnlyList<ITableEntry> entries, bool removeAllEntries)
        {
            lock (_sync)
            {
                if (removeAllEntries)
                {
                    _items.RemoveAll(item => item is ITableEntry);
                }

                _items.AddRange(entries);
            }

            MarkChanged();
        }

        public void RemoveEntries(IReadOnlyList<ITableEntry> entries)
        {
            lock (_sync)
            {
                foreach (var entry in entries)
                {
                    _items.Remove(entry);
                }
            }

            MarkChanged();
        }

        public void ReplaceEntries(IReadOnlyList<ITableEntry> oldEntries, IReadOnlyList<ITableEntry> newEntries)
        {
            RemoveEntries(oldEntries);
            AddEntries(newEntries, false);
        }

        public void RemoveAllEntries()
        {
            lock (_sync)
            {
                _items.RemoveAll(item => item is ITableEntry);
            }

            MarkChanged();
        }

        public void AddSnapshot(ITableEntriesSnapshot snapshot, bool removeAllSnapshots)
        {
            lock (_sync)
            {
                if (removeAllSnapshots)
                {
                    _items.RemoveAll(item => item is ITableEntriesSnapshot);
                }

                _items.Add(snapshot);
            }

            MarkChanged();
        }

        public void RemoveSnapshot(ITableEntriesSnapshot snapshot)
        {
            lock (_sync)
            {
                _items.Remove(snapshot);
            }

            MarkChanged();
        }

        public void RemoveAllSnapshots()
        {
            lock (_sync)
            {
                _items.RemoveAll(item => item is ITableEntriesSnapshot);
            }

            MarkChanged();
        }

        public void ReplaceSnapshot(ITableEntriesSnapshot oldSnapshot, ITableEntriesSnapshot newSnapshot)
        {
            ReplaceItem(oldSnapshot, newSnapshot);
        }

        public void AddFactory(ITableEntriesSnapshotFactory factory, bool removeAllFactories)
        {
            lock (_sync)
            {
                if (removeAllFactories)
                {
                    _items.RemoveAll(item => item is ITableEntriesSnapshotFactory);
                }

                _items.Add(factory);
            }

            MarkChanged();
        }

        public void RemoveFactory(ITableEntriesSnapshotFactory factory)
        {
            lock (_sync)
            {
                _items.Remove(factory);
            }

            MarkChanged();
        }

        public void ReplaceFactory(ITableEntriesSnapshotFactory oldFactory, ITableEntriesSnapshotFactory newFactory)
        {
            ReplaceItem(oldFactory, newFactory);
        }

        public void FactorySnapshotChanged(ITableEntriesSnapshotFactory factory)
        {
            MarkChanged();
        }

        public void RemoveAllFactories()
        {
            lock (_sync)
            {
                _items.RemoveAll(item => item is ITableEntriesSnapshotFactory);
            }

            MarkChanged();
        }

        public IEnumerable<ITableEntry> ReadEntries()
        {
            List<object> items;
            lock (_sync)
            {
                items = new List<object>(_items);
            }

            foreach (var item in items)
            {
                if (item is ITableEntry entry)
                {
                    yield return entry;
                    continue;
                }

                var snapshot = item is ITableEntriesSnapshotFactory factory
                    ? factory.GetCurrentSnapshot()
                    : item as ITableEntriesSnapshot;
                if (snapshot is null)
                {
                    continue;
                }

                snapshot.StartCaching();
                try
                {
                    for (var index = 0; index < snapshot.Count; index++)
                    {
                        yield return new SnapshotEntry(snapshot, index);
                    }
                }
                finally
                {
                    snapshot.StopCaching();
                }
            }
        }

        public async Task WaitForQuiescenceAsync(
            TimeSpan quietPeriod,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var startedAt = DateTime.UtcNow;
            while (DateTime.UtcNow - startedAt < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Volatile.Read(ref _hasSignal) != 0 &&
                    DateTime.UtcNow.Ticks - Volatile.Read(ref _lastChangeTicks) >= quietPeriod.Ticks)
                {
                    return;
                }

                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }

        private void ReplaceItem(object oldItem, object newItem)
        {
            lock (_sync)
            {
                var index = _items.IndexOf(oldItem);
                if (index >= 0)
                {
                    _items[index] = newItem;
                }
                else
                {
                    _items.Add(newItem);
                }
            }

            MarkChanged();
        }

        private void MarkChanged()
        {
            Volatile.Write(ref _lastChangeTicks, DateTime.UtcNow.Ticks);
            Volatile.Write(ref _hasSignal, 1);
        }
    }

    private sealed class SnapshotEntry : ITableEntry
    {
        private readonly ITableEntriesSnapshot _snapshot;
        private readonly int _index;

        public SnapshotEntry(ITableEntriesSnapshot snapshot, int index)
        {
            _snapshot = snapshot;
            _index = index;
        }

        public object Identity => this;

        public bool TryGetValue(string keyName, out object content) =>
            _snapshot.TryGetValue(_index, keyName, out content);

        public bool CanSetValue(string keyName) => false;

        public bool TrySetValue(string keyName, object content) => false;
    }

    private sealed class ErrorFilters
    {
        public ErrorFilters(HashSet<string> severities, string? project, string? file, int maxCount)
        {
            Severities = severities;
            Project = project;
            File = file;
            MaxCount = maxCount;
        }

        public HashSet<string> Severities { get; }

        public string? Project { get; }

        public string? File { get; }

        public int MaxCount { get; }
    }
}

internal sealed class DiagnosticsProviderException : Exception
{
    public DiagnosticsProviderException(Exception? innerException = null)
        : this(BridgeErrorCodes.DiagnosticsUnavailable, true, innerException)
    {
    }

    private DiagnosticsProviderException(string code, bool retryable, Exception? innerException)
        : base("The Visual Studio diagnostics snapshot is unavailable.", innerException)
    {
        Code = code;
        Retryable = retryable;
    }

    public string Code { get; }

    public bool Retryable { get; }

    public static DiagnosticsProviderException InvalidRequest() =>
        new(BridgeErrorCodes.InvalidRequest, false, null);
}