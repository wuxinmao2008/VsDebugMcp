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

internal sealed class SolutionFileProvider
{
	private static readonly Guid SolutionFolderTypeGuid = new("66A26720-8FB5-11D2-AA7E-00C04F688DDE");
	private readonly AsyncPackage _package;
	private readonly string _vsInstanceId;

	public SolutionFileProvider(AsyncPackage package, string vsInstanceId)
	{
		_package = package;
		_vsInstanceId = vsInstanceId;
	}

	public async Task<GetFilesInProjectResponse> GetFilesInProjectAsync(
		GetFilesInProjectRequest request,
		CancellationToken cancellationToken)
	{
		await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
		var solution = await _package.GetServiceAsync(typeof(SVsSolution)) as IVsSolution
			?? throw new SolutionStateUnavailableException();

		try
		{
			var response = new GetFilesInProjectResponse
			{
				VsInstanceId = _vsInstanceId
			};

			var isOpen = ReadSolutionProperty(solution, __VSPROPID.VSPROPID_IsSolutionOpen, false);
			if (!isOpen)
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

			var extensionFilter = ParseExtensionFilter(request.ExtensionFilter);
			var requestedProjectId = request.ProjectId?.Trim();

			var items = new IVsHierarchy[1];
			var projectIndex = 0;
			var matchedAnyProject = false;

			while (hierarchies.Next(1, items, out var fetched) == VSConstants.S_OK && fetched == 1)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var hierarchy = items[0];
				if (hierarchy is null || IsSolutionFolder(hierarchy))
				{
					continue;
				}

				projectIndex++;
				var projectInfo = ReadProjectBasicInfo(solution, hierarchy, projectIndex);

				if (!string.IsNullOrEmpty(requestedProjectId) && !ProjectMatches(projectInfo, requestedProjectId!))
				{
					continue;
				}

				matchedAnyProject = true;
				var files = new List<ProjectFileInfo>();
				var visited = new HashSet<uint>();

				try
				{
					TraverseHierarchy(
						hierarchy,
						VSConstants.VSITEMID_ROOT,
						projectInfo.ProjectFilePath,
						projectInfo.ProjectDirectory,
						currentFilter: string.Empty,
						extensionFilter,
						files,
						visited,
						cancellationToken);
				}
				catch (Exception exception) when (exception is not OutOfMemoryException && exception is not OperationCanceledException)
				{
					response.Warnings.Add(new BridgeWarning
					{
						Code = "project_files_traversal_partial",
						Message = $"Partial failure reading files for project '{projectInfo.Name}': {exception.Message}",
						ProjectId = projectInfo.Id
					});
				}

				response.Projects.Add(new ProjectFilesGroup
				{
					ProjectId = projectInfo.Id,
					ProjectName = projectInfo.Name,
					ProjectFilePath = projectInfo.ProjectFilePath,
					Files = files,
					FileCount = files.Count
				});

				response.TotalFileCount += files.Count;
			}

			if (!string.IsNullOrEmpty(requestedProjectId) && !matchedAnyProject)
			{
				response.Warnings.Add(new BridgeWarning
				{
					Code = BridgeErrorCodes.ProjectNotFound,
					Message = $"No loaded project matched '{requestedProjectId}'.",
					ProjectId = requestedProjectId
				});
			}

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

	private static void TraverseHierarchy(
		IVsHierarchy hierarchy,
		uint currentItemId,
		string projectFilePath,
		string projectDirectory,
		string currentFilter,
		HashSet<string>? extensionFilter,
		List<ProjectFileInfo> files,
		HashSet<uint> visited,
		CancellationToken cancellationToken)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		cancellationToken.ThrowIfCancellationRequested();

		if (!visited.Add(currentItemId))
		{
			return;
		}

		var project = hierarchy as IVsProject;

		var result = hierarchy.GetProperty(currentItemId, (int)__VSHPROPID.VSHPROPID_FirstChild, out var firstChildObj);
		if (ErrorHandler.Failed(result) || firstChildObj is null)
		{
			return;
		}

		var childId = ConvertToItemId(firstChildObj);
		while (childId != VSConstants.VSITEMID_NIL)
		{
			cancellationToken.ThrowIfCancellationRequested();

			hierarchy.GetProperty(childId, (int)__VSHPROPID.VSHPROPID_Caption, out var captionObj);
			var caption = captionObj as string ?? string.Empty;

			// Skip IDE virtual folders that are not part of project sources (e.g. External Dependencies / 外部依赖项)
			if (IsExternalDependenciesNode(caption))
			{
				hierarchy.GetProperty(childId, (int)__VSHPROPID.VSHPROPID_NextSibling, out var skipNextObj);
				childId = ConvertToItemId(skipNextObj);
				continue;
			}

			string docPath = string.Empty;
			if (project is not null &&
				ErrorHandler.Succeeded(project.GetMkDocument(childId, out var mkDoc)) &&
				!string.IsNullOrWhiteSpace(mkDoc))
			{
				docPath = mkDoc;
			}

			if (!string.IsNullOrEmpty(docPath) &&
				!string.Equals(docPath, projectFilePath, StringComparison.OrdinalIgnoreCase) &&
				File.Exists(docPath))
			{
				var ext = Path.GetExtension(docPath);
				if (extensionFilter is null || extensionFilter.Contains(ext.ToLowerInvariant()))
				{
					files.Add(new ProjectFileInfo
					{
						FilePath = docPath,
						RelativePath = MakeRelativePath(projectDirectory, docPath),
						FilterPath = string.IsNullOrEmpty(currentFilter) ? null : currentFilter,
						Extension = ext
					});
				}

				// Check for nested children under this file (e.g. Form1.Designer.cs under Form1.cs)
				TraverseHierarchy(
					hierarchy,
					childId,
					projectFilePath,
					projectDirectory,
					currentFilter,
					extensionFilter,
					files,
					visited,
					cancellationToken);
			}
			else
			{
				var nextFilter = string.IsNullOrEmpty(currentFilter)
					? caption
					: (string.IsNullOrEmpty(caption) ? currentFilter : $"{currentFilter}/{caption}");

				TraverseHierarchy(
					hierarchy,
					childId,
					projectFilePath,
					projectDirectory,
					nextFilter,
					extensionFilter,
					files,
					visited,
					cancellationToken);
			}

			hierarchy.GetProperty(childId, (int)__VSHPROPID.VSHPROPID_NextSibling, out var nextSiblingObj);
			childId = ConvertToItemId(nextSiblingObj);
		}
	}

	private static bool IsExternalDependenciesNode(string caption) =>
		string.Equals(caption, "External Dependencies", StringComparison.OrdinalIgnoreCase) ||
		string.Equals(caption, "外部依赖项", StringComparison.OrdinalIgnoreCase);

	private static uint ConvertToItemId(object? value)
	{
		if (value is null)
		{
			return VSConstants.VSITEMID_NIL;
		}

		if (value is int intVal)
		{
			return unchecked((uint)intVal);
		}

		if (value is uint uintVal)
		{
			return uintVal;
		}

		if (value is short shortVal)
		{
			return unchecked((uint)shortVal);
		}

		if (value is ushort ushortVal)
		{
			return ushortVal;
		}

		return VSConstants.VSITEMID_NIL;
	}

	private static string MakeRelativePath(string basePath, string fullPath)
	{
		if (string.IsNullOrWhiteSpace(basePath) || string.IsNullOrWhiteSpace(fullPath))
		{
			return fullPath;
		}

		try
		{
			if (!basePath.EndsWith("\\", StringComparison.Ordinal) && !basePath.EndsWith("/", StringComparison.Ordinal))
			{
				basePath += "\\";
			}

			var baseUri = new Uri(basePath);
			var fullUri = new Uri(fullPath);

			if (baseUri.Scheme != fullUri.Scheme)
			{
				return fullPath;
			}

			var relativeUri = baseUri.MakeRelativeUri(fullUri);
			var relativePath = Uri.UnescapeDataString(relativeUri.ToString());
			return relativePath.Replace('/', '\\');
		}
		catch
		{
			return Path.GetFileName(fullPath);
		}
	}

	private static HashSet<string>? ParseExtensionFilter(string? filter)
	{
		if (string.IsNullOrWhiteSpace(filter))
		{
			return null;
		}

		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var parts = filter!.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
		foreach (var part in parts)
		{
			var trimmed = part.Trim();
			if (trimmed.Length == 0)
			{
				continue;
			}

			if (!trimmed.StartsWith(".", StringComparison.Ordinal))
			{
				trimmed = "." + trimmed;
			}

			set.Add(trimmed.ToLowerInvariant());
		}

		return set.Count > 0 ? set : null;
	}

	private static bool ProjectMatches(SolutionProjectInfo projectInfo, string requestedId)
	{
		if (string.Equals(projectInfo.Id, requestedId, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		if (string.Equals(projectInfo.Name, requestedId, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		if (string.Equals(projectInfo.ProjectGuid, requestedId, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		if (string.Equals(projectInfo.ProjectFilePath, requestedId, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		if (!string.IsNullOrEmpty(projectInfo.ProjectFilePath) &&
			string.Equals(Path.GetFileName(projectInfo.ProjectFilePath), requestedId, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return false;
	}

	private static SolutionProjectInfo ReadProjectBasicInfo(
		IVsSolution solution,
		IVsHierarchy hierarchy,
		int projectIndex)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		var name = ReadHierarchyString(hierarchy, __VSHPROPID.VSHPROPID_Name);
		var projectGuid = GetProjectGuid(solution, hierarchy);
		var path = GetProjectPath(hierarchy);
		var id = projectGuid.Length > 0
			? projectGuid
			: $"project:{projectIndex.ToString(CultureInfo.InvariantCulture)}:{name}";

		return new SolutionProjectInfo
		{
			Id = id,
			Name = name,
			ProjectFilePath = path,
			ProjectDirectory = path.Length > 0 ? Path.GetDirectoryName(path) ?? string.Empty : string.Empty,
			ProjectGuid = projectGuid,
			IsLoaded = true
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

	private static bool IsSolutionFolder(IVsHierarchy hierarchy)
	{
		ThreadHelper.ThrowIfNotOnUIThread();
		return ErrorHandler.Succeeded(
			hierarchy.GetGuidProperty(
				VSConstants.VSITEMID_ROOT,
				(int)__VSHPROPID.VSHPROPID_TypeGuid,
				out var typeGuid)) && typeGuid == SolutionFolderTypeGuid;
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
}
