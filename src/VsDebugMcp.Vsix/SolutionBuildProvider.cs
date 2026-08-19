using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using VsDebugMcp.Protocol;

namespace VsDebugMcp_Vsix;

internal sealed class SolutionBuildProvider : IVsUpdateSolutionEvents, IDisposable
{
	private const int MaxConfigurationValueLength = 256;
	private readonly AsyncPackage _package;
	private readonly string _vsInstanceId;
	private readonly object _sync = new();
	private IVsSolutionBuildManager2? _buildManager;
	private DTE2? _dte;
	private uint _eventsCookie;
	private BuildTaskResponse? _build;
	private bool _disposed;

	public SolutionBuildProvider(AsyncPackage package, string vsInstanceId)
	{
		_package = package;
		_vsInstanceId = vsInstanceId;
	}

	public async Task InitializeAsync(CancellationToken cancellationToken)
	{
		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		_buildManager = await _package.GetServiceAsync(typeof(SVsSolutionBuildManager)) as IVsSolutionBuildManager2
			?? throw new InvalidOperationException("The Visual Studio solution build manager is unavailable.");
		_dte = await _package.GetServiceAsync(typeof(DTE)) as DTE2
			?? throw new InvalidOperationException("The Visual Studio automation service is unavailable.");

		ErrorHandler.ThrowOnFailure(_buildManager.AdviseUpdateSolutionEvents(this, out _eventsCookie));
	}

	public async Task<BuildTaskResponse> RunBuildAsync(
		RunBuildRequest request,
		CancellationToken cancellationToken)
	{
		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var buildManager = GetBuildManager();
		var dte = GetDte();
		ValidateBuildRequest(request);

		if (!dte.Solution.IsOpen)
		{
			throw new BuildProviderException(BridgeErrorCodes.SolutionNotOpen, false);
		}

		lock (_sync)
		{
			if (_build is not null && !IsTerminal(_build.State))
			{
				throw new BuildProviderException(BridgeErrorCodes.BuildInProgress, true);
			}
		}

		ErrorHandler.ThrowOnFailure(buildManager.QueryBuildManagerBusy(out var busy));
		if (busy != 0)
		{
			throw new BuildProviderException(BridgeErrorCodes.BuildInProgress, true);
		}

		var configuration = ResolveConfiguration(dte, request);
		configuration.Activate();
		VerifyActiveConfiguration(dte, configuration);

		var build = new BuildTaskResponse
		{
			BuildTaskId = Guid.NewGuid().ToString("N"),
			VsInstanceId = _vsInstanceId,
			State = BuildStates.Starting,
			Configuration = configuration.Name,
			Platform = configuration.PlatformName,
			RequestedAtUtc = UtcNow()
		};

		lock (_sync)
		{
			_build = build;
		}

		var result = buildManager.StartSimpleUpdateSolutionConfiguration(
			(uint)VSSOLNBUILDUPDATEFLAGS.SBF_OPERATION_BUILD,
			(uint)VSSOLNBUILDQUERYRESULTS.VSSBQR_SAVEBEFOREBUILD_QUERY_YES,
			1);
		if (ErrorHandler.Failed(result))
		{
			lock (_sync)
			{
				if (ReferenceEquals(_build, build))
				{
					_build = null;
				}
			}

			throw new BuildProviderException(BridgeErrorCodes.BuildStartFailed, false);
		}

		return Snapshot(build);
	}

	public BuildTaskResponse GetBuildStatus(string buildTaskId)
	{
		ThrowIfDisposed();
		if (string.IsNullOrWhiteSpace(buildTaskId))
		{
			throw new BuildProviderException(BridgeErrorCodes.InvalidRequest, false);
		}

		lock (_sync)
		{
			if (_build is null || !string.Equals(_build.BuildTaskId, buildTaskId, StringComparison.Ordinal))
			{
				throw new BuildProviderException(BridgeErrorCodes.BuildTaskNotFound, false);
			}

			return Snapshot(_build);
		}
	}

	public async Task<CancelBuildResponse> CancelBuildAsync(
		string buildTaskId,
		CancellationToken cancellationToken)
	{
		ThrowIfDisposed();
		if (string.IsNullOrWhiteSpace(buildTaskId))
		{
			throw new BuildProviderException(BridgeErrorCodes.InvalidRequest, false);
		}

		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var buildManager = GetBuildManager();
		BuildTaskResponse build;

		lock (_sync)
		{
			if (_build is null || !string.Equals(_build.BuildTaskId, buildTaskId, StringComparison.Ordinal))
			{
				throw new BuildProviderException(BridgeErrorCodes.BuildTaskNotFound, false);
			}

			if (IsTerminal(_build.State))
			{
				throw new BuildProviderException(BridgeErrorCodes.BuildNotActive, false);
			}

			build = _build;
		}

		ErrorHandler.ThrowOnFailure(buildManager.CanCancelUpdateSolutionConfiguration(out var canCancel));
		if (canCancel == 0)
		{
			throw new BuildProviderException(BridgeErrorCodes.BuildCancelNotSupported, false);
		}

		lock (_sync)
		{
			build.CancelRequested = true;
			build.State = BuildStates.Cancelling;
		}

		var result = buildManager.CancelUpdateSolutionConfiguration();
		if (ErrorHandler.Failed(result))
		{
			lock (_sync)
			{
				if (ReferenceEquals(_build, build) && !IsTerminal(build.State))
				{
					build.CancelRequested = false;
					build.State = BuildStates.Running;
				}
			}

			throw new BuildProviderException(BridgeErrorCodes.BuildStateUnavailable, true);
		}

		return new CancelBuildResponse
		{
			Accepted = true,
			Build = Snapshot(build)
		};
	}

	public int UpdateSolution_Begin(ref int pfCancelUpdate)
	{
		TransitionToRunning();
		return VSConstants.S_OK;
	}

	public int UpdateSolution_StartUpdate(ref int pfCancelUpdate)
	{
		TransitionToRunning();
		return VSConstants.S_OK;
	}

	public int UpdateSolution_Cancel()
	{
		lock (_sync)
		{
			if (_build is not null && !IsTerminal(_build.State))
			{
				_build.CancelRequested = true;
				_build.State = BuildStates.Cancelling;
			}
		}

		return VSConstants.S_OK;
	}

	public int UpdateSolution_Done(int fSucceeded, int fModified, int fCancelCommand)
	{
		lock (_sync)
		{
			if (_build is null || IsTerminal(_build.State))
			{
				return VSConstants.S_OK;
			}

			var cancelled = _build.CancelRequested || (fSucceeded == 0 && fCancelCommand != 0);
			_build.State = cancelled
				? BuildStates.Cancelled
				: fSucceeded != 0 ? BuildStates.Succeeded : BuildStates.Failed;
			_build.Succeeded = cancelled ? false : fSucceeded != 0;
			_build.CompletedAtUtc = UtcNow();
		}

		return VSConstants.S_OK;
	}

	public int OnActiveProjectCfgChange(IVsHierarchy pIVsHierarchy) => VSConstants.S_OK;

	public void Dispose()
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		lock (_sync)
		{
			_disposed = true;
		}

		if (_buildManager is not null && _eventsCookie != 0)
		{
			_buildManager.UnadviseUpdateSolutionEvents(_eventsCookie);
			_eventsCookie = 0;
		}
	}

	private void TransitionToRunning()
	{
		lock (_sync)
		{
			if (_build is null || IsTerminal(_build.State) || _build.State == BuildStates.Cancelling)
			{
				return;
			}

			_build.State = BuildStates.Running;
			_build.StartedAtUtc ??= UtcNow();
		}
	}

	private static SolutionConfiguration2 ResolveConfiguration(DTE2 dte, RunBuildRequest request)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		var solution = dte.Solution
			?? throw new BuildProviderException(BridgeErrorCodes.BuildStateUnavailable, true);
		var solutionBuild = solution.SolutionBuild
			?? throw new BuildProviderException(BridgeErrorCodes.BuildStateUnavailable, true);
		var active = solutionBuild.ActiveConfiguration as SolutionConfiguration2
			?? throw new BuildProviderException(BridgeErrorCodes.BuildStateUnavailable, true);
		var requestedName = string.IsNullOrWhiteSpace(request.Configuration)
			? active.Name
			: request.Configuration!.Trim();
		var requestedPlatform = string.IsNullOrWhiteSpace(request.Platform)
			? active.PlatformName
			: request.Platform!.Trim();

		var configurations = solutionBuild.SolutionConfigurations
			?? throw new BuildProviderException(BridgeErrorCodes.BuildStateUnavailable, true);
		foreach (SolutionConfiguration item in configurations)
		{
			if (item is SolutionConfiguration2 candidate &&
				string.Equals(candidate.Name, requestedName, StringComparison.OrdinalIgnoreCase) &&
				string.Equals(candidate.PlatformName, requestedPlatform, StringComparison.OrdinalIgnoreCase))
			{
				return candidate;
			}
		}

		throw new BuildProviderException(BridgeErrorCodes.InvalidBuildConfiguration, false);
	}

	private static void VerifyActiveConfiguration(DTE2 dte, SolutionConfiguration2 expected)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		var active = dte.Solution?.SolutionBuild?.ActiveConfiguration as SolutionConfiguration2;
		if (active is null ||
			!string.Equals(active.Name, expected.Name, StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(active.PlatformName, expected.PlatformName, StringComparison.OrdinalIgnoreCase))
		{
			throw new BuildProviderException(BridgeErrorCodes.BuildStartFailed, false);
		}
	}

	private static void ValidateBuildRequest(RunBuildRequest request)
	{
		if ((request.Configuration?.Length ?? 0) > MaxConfigurationValueLength ||
			(request.Platform?.Length ?? 0) > MaxConfigurationValueLength)
		{
			throw new BuildProviderException(BridgeErrorCodes.InvalidRequest, false);
		}
	}

	private IVsSolutionBuildManager2 GetBuildManager() =>
		!_disposed && _buildManager is not null
			? _buildManager
			: throw new BuildProviderException(BridgeErrorCodes.BuildStateUnavailable, true);

	private DTE2 GetDte() =>
		!_disposed && _dte is not null
			? _dte
			: throw new BuildProviderException(BridgeErrorCodes.BuildStateUnavailable, true);

	private void ThrowIfDisposed()
	{
		lock (_sync)
		{
			if (_disposed)
			{
				throw new BuildProviderException(BridgeErrorCodes.BuildStateUnavailable, true);
			}
		}
	}

	private static bool IsTerminal(string state) =>
		state == BuildStates.Succeeded || state == BuildStates.Failed || state == BuildStates.Cancelled;

	private static BuildTaskResponse Snapshot(BuildTaskResponse build) => new()
	{
		BuildTaskId = build.BuildTaskId,
		VsInstanceId = build.VsInstanceId,
		State = build.State,
		Configuration = build.Configuration,
		Platform = build.Platform,
		RequestedAtUtc = build.RequestedAtUtc,
		StartedAtUtc = build.StartedAtUtc,
		CompletedAtUtc = build.CompletedAtUtc,
		Succeeded = build.Succeeded,
		CancelRequested = build.CancelRequested
	};

	private static string UtcNow() => DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
}

internal sealed class BuildProviderException : Exception
{
	public BuildProviderException(string code, bool retryable)
		: base(code)
	{
		Code = code;
		Retryable = retryable;
	}

	public string Code { get; }

	public bool Retryable { get; }
}