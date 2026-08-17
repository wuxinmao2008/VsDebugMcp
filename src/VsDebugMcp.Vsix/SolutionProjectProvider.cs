using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using VsDebugMcp.Protocol;

namespace VsDebugMcp_Vsix;

internal sealed class SolutionProjectProvider
{
	private static readonly Guid SolutionFolderTypeGuid = new("66A26720-8FB5-11D2-AA7E-00C04F688DDE");
	private readonly AsyncPackage _package;

	public SolutionProjectProvider(AsyncPackage package)
	{
		_package = package;
	}

	public async Task<GetProjectsInSolutionResponse> GetProjectsAsync(CancellationToken cancellationToken)
	{
		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var solution = await _package.GetServiceAsync(typeof(SVsSolution)) as IVsSolution
			?? throw new SolutionStateUnavailableException();

		try
		{
			var response = new GetProjectsInSolutionResponse
			{
				VsInstanceId = ProcessId.ToString(CultureInfo.InvariantCulture),
				Solution = ReadSolutionInfo(solution)
			};

			if (!response.Solution.IsOpen)
			{
				return response;
			}

			var onlyThisType = Guid.Empty;
			var result = solution.GetProjectEnum(
				(uint)__VSENUMPROJFLAGS.EPF_LOADEDINSOLUTION,
				ref onlyThisType,
				out var hierarchies);
			if (ErrorHandler.Failed(result) || hierarchies is null)
			{
				throw new SolutionStateUnavailableException();
			}

			var items = new IVsHierarchy[1];
			var projectIndex = 0;
			while (hierarchies.Next(1, items, out var fetched) == VSConstants.S_OK && fetched == 1)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var hierarchy = items[0];
				if (hierarchy is null || IsSolutionFolder(hierarchy))
				{
					continue;
				}

				projectIndex++;
				try
				{
					response.Projects.Add(ReadProject(solution, hierarchy, projectIndex, response.Warnings));
				}
				catch (Exception exception) when (exception is not OutOfMemoryException)
				{
					var id = $"project:{projectIndex.ToString(CultureInfo.InvariantCulture)}";
					response.Projects.Add(new SolutionProjectInfo
					{
						Id = id,
						Kind = "unknown",
						IsLoaded = true,
						IsUnsupported = true
					});
					response.Warnings.Add(
						CreateWarning("project_read_failed", "The project metadata could not be read.", id));
				}
			}

			response.Solution.ProjectCount = response.Projects.Count;
			return response;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (SolutionStateUnavailableException)
		{
			throw;
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			throw new SolutionStateUnavailableException(exception);
		}
	}

	private static int ProcessId => System.Diagnostics.Process.GetCurrentProcess().Id;

	private static SolutionInfo ReadSolutionInfo(IVsSolution solution)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		var isOpen = ReadSolutionProperty(solution, __VSPROPID.VSPROPID_IsSolutionOpen, false);
		return new SolutionInfo
		{
			IsOpen = isOpen,
			Name = ReadSolutionProperty(solution, __VSPROPID.VSPROPID_SolutionBaseName, string.Empty),
			FilePath = ReadSolutionProperty(solution, __VSPROPID.VSPROPID_SolutionFileName, string.Empty),
			Directory = ReadSolutionProperty(solution, __VSPROPID.VSPROPID_SolutionDirectory, string.Empty)
		};
	}

	private static T ReadSolutionProperty<T>(IVsSolution solution, __VSPROPID property, T fallback)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		var result = solution.GetProperty((int)property, out var value);
		if (ErrorHandler.Failed(result) || value is null)
		{
			return fallback;
		}

		try
		{
			return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
		}
		catch
		{
			return fallback;
		}
	}

	private static bool IsSolutionFolder(IVsHierarchy hierarchy)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		return TryGetTypeGuid(hierarchy, out var typeGuid) && typeGuid == SolutionFolderTypeGuid;
	}

	private static SolutionProjectInfo ReadProject(
		IVsSolution solution,
		IVsHierarchy hierarchy,
		int projectIndex,
		List<BridgeWarning> warnings)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		var name = ReadHierarchyString(hierarchy, __VSHPROPID.VSHPROPID_Name);
		var projectGuid = GetProjectGuid(solution, hierarchy);
		var typeGuid = TryGetTypeGuid(hierarchy, out var parsedTypeGuid)
			? parsedTypeGuid.ToString("D")
			: string.Empty;
		var path = GetProjectPath(hierarchy);
		var id = projectGuid.Length > 0
			? projectGuid
			: $"project:{projectIndex.ToString(CultureInfo.InvariantCulture)}:{name}:{typeGuid}";
		var unsupported = false;

		if (projectGuid.Length == 0)
		{
			unsupported = true;
			warnings.Add(CreateWarning("project_guid_unavailable", "The project GUID is unavailable.", id));
		}

		if (typeGuid.Length == 0)
		{
			unsupported = true;
			warnings.Add(CreateWarning("project_type_unavailable", "The project type is unavailable.", id));
		}

		if (path.Length == 0)
		{
			unsupported = true;
			warnings.Add(CreateWarning("project_path_unavailable", "The project file path is unavailable.", id));
		}

		return new SolutionProjectInfo
		{
			Id = id,
			Name = name,
			ProjectFilePath = path,
			ProjectDirectory = path.Length > 0 ? Path.GetDirectoryName(path) ?? string.Empty : string.Empty,
			ProjectGuid = projectGuid,
			TypeGuid = typeGuid,
			Kind = typeGuid.Length > 0 ? "project" : "unknown",
			IsLoaded = true,
			IsUnsupported = unsupported
		};
	}

	private static string ReadHierarchyString(IVsHierarchy hierarchy, __VSHPROPID property)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		var result = hierarchy.GetProperty(VSConstants.VSITEMID_ROOT, (int)property, out var value);
		return ErrorHandler.Succeeded(result) ? value as string ?? string.Empty : string.Empty;
	}

	private static string GetProjectGuid(IVsSolution solution, IVsHierarchy hierarchy)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		var result = solution.GetGuidOfProject(hierarchy, out var projectGuid);
		return ErrorHandler.Succeeded(result) && projectGuid != Guid.Empty
			? projectGuid.ToString("D")
			: string.Empty;
	}

	private static bool TryGetTypeGuid(IVsHierarchy hierarchy, out Guid typeGuid)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		return ErrorHandler.Succeeded(
			hierarchy.GetGuidProperty(
				VSConstants.VSITEMID_ROOT,
				(int)__VSHPROPID.VSHPROPID_TypeGuid,
				out typeGuid)) && typeGuid != Guid.Empty;
	}

	private static string GetProjectPath(IVsHierarchy hierarchy)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		if (hierarchy is IVsProject project &&
			ErrorHandler.Succeeded(project.GetMkDocument(VSConstants.VSITEMID_ROOT, out var projectFilePath)) &&
			!string.IsNullOrWhiteSpace(projectFilePath) &&
			Path.IsPathRooted(projectFilePath))
		{
			return projectFilePath;
		}

		return string.Empty;
	}

	private static BridgeWarning CreateWarning(string code, string message, string projectId) => new()
	{
		Code = code,
		Message = message,
		ProjectId = projectId
	};
}

internal sealed class SolutionStateUnavailableException : Exception
{
	public SolutionStateUnavailableException()
	{
	}

	public SolutionStateUnavailableException(Exception innerException)
		: base("The Visual Studio solution state is unavailable.", innerException)
	{
	}
}