using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
	private readonly string _vsInstanceId;

	public SolutionProjectProvider(AsyncPackage package, string vsInstanceId)
	{
		_package = package;
		_vsInstanceId = vsInstanceId;
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
				VsInstanceId = _vsInstanceId,
				Solution = await ReadSolutionInfoAsync(solution, cancellationToken)
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

			EnrichCMakeWorkspace(response);

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

	private static void EnrichCMakeWorkspace(GetProjectsInSolutionResponse response)
	{
		var solutionDir = response.Solution.Directory;
		if (string.IsNullOrWhiteSpace(solutionDir))
		{
			return;
		}

		var cmakeListsPath = Path.Combine(solutionDir, "CMakeLists.txt");
		if (!File.Exists(cmakeListsPath))
		{
			return;
		}

		var hasConventionalProject = false;
		foreach (var project in response.Projects)
		{
			if (!project.IsUnsupported && !string.IsNullOrWhiteSpace(project.ProjectFilePath))
			{
				hasConventionalProject = true;
				break;
			}
		}

		if (!hasConventionalProject)
		{
			response.Projects.RemoveAll(p =>
				string.Equals(p.TypeGuid, "6bb5f8f0-4483-11d3-8bcf-00c04f8ec28c", StringComparison.OrdinalIgnoreCase) ||
				string.IsNullOrWhiteSpace(p.ProjectFilePath));

			response.Warnings.RemoveAll(w =>
				string.Equals(w.Code, "project_path_unavailable", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(w.Code, "project_type_unavailable", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(w.Code, "project_guid_unavailable", StringComparison.OrdinalIgnoreCase));

			var cleanDir = solutionDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			var projectName = string.IsNullOrWhiteSpace(response.Solution.Name)
				? Path.GetFileName(cleanDir)
				: response.Solution.Name;

			response.Projects.Add(new SolutionProjectInfo
			{
				Id = "cmake:root",
				Name = projectName,
				ProjectFilePath = cmakeListsPath,
				ProjectDirectory = cleanDir,
				ProjectGuid = "cmake:root",
				TypeGuid = "cmake",
				Kind = "cmake",
				IsLoaded = true,
				IsUnsupported = false
			});
		}
	}

	private async Task<SolutionInfo> ReadSolutionInfoAsync(IVsSolution solution, CancellationToken cancellationToken)
	{
		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var isOpen = ReadSolutionProperty(solution, __VSPROPID.VSPROPID_IsSolutionOpen, false);
		var baseName = ReadSolutionProperty(solution, __VSPROPID.VSPROPID_SolutionBaseName, string.Empty);
		var fileName = ReadSolutionProperty(solution, __VSPROPID.VSPROPID_SolutionFileName, string.Empty);
		var directory = ReadSolutionProperty(solution, __VSPROPID.VSPROPID_SolutionDirectory, string.Empty);

		if (!isOpen || string.IsNullOrWhiteSpace(directory))
		{
			try
			{
				var dte = await _package.GetServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE80.DTE2;
				if (dte?.Solution != null)
				{
					var dteFullName = dte.Solution.FullName;
					if (!string.IsNullOrWhiteSpace(dteFullName) && Directory.Exists(dteFullName))
					{
						var cmakeLists = Path.Combine(dteFullName, "CMakeLists.txt");
						if (File.Exists(cmakeLists))
						{
							isOpen = true;
							var cleanPath = dteFullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
							if (string.IsNullOrWhiteSpace(baseName))
							{
								baseName = Path.GetFileName(cleanPath);
							}
							if (string.IsNullOrWhiteSpace(fileName))
							{
								fileName = cmakeLists;
							}
							directory = cleanPath + Path.DirectorySeparatorChar;
						}
					}
				}
			}
			catch
			{
			}
		}

		return new SolutionInfo
		{
			IsOpen = isOpen,
			Name = baseName,
			FilePath = fileName,
			Directory = directory
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