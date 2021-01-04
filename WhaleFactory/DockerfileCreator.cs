namespace WhaleFactory
{
    //TODO: delete
    //internal class DockerfileCreator : IDisposable
    //{
    //    private TextWriter output;
    //    private DockerFileOptions options;
    //    private readonly ILogger logger;
    //    private MSBuildWorkspace workspace;
    //    private Solution solution;
    //    private Dictionary<FileInfoKey, Project> allProjects;
    //    private Dictionary<FileInfoKey, string> projectFrameworks;
    //    private ProjectDependencyGraph solutionGraph;
    //    private ProjectId[] sortedProjects;
    //    private Dictionary<ProjectId, Project> idToProject;

    //    public DockerfileCreator(TextWriter output, DockerFileOptions options, ILogger logger)
    //    {
    //        this.output = output;
    //        this.options = options;
    //        this.logger = logger;
    //        this.workspace = MSBuildWorkspace.Create();
    //    }

    //    public async Task Load()
    //    {
    //        this.workspace.WorkspaceFailed += (sender, args) =>
    //        {
    //            if (args.Diagnostic.Kind == WorkspaceDiagnosticKind.Warning)
    //            {
    //                this.logger.Warning(args.Diagnostic.Message);
    //            }
    //            else
    //            {
    //                this.logger.Error(args.Diagnostic.Message);
    //            }
    //        };
    //        this.projectFrameworks = new Dictionary<FileInfoKey, string>();
    //        var projLoadHandler = new Progress<ProjectLoadProgress>(
    //            progress =>
    //            {
    //                this.projectFrameworks[new FileInfoKey(progress.FilePath)] = progress.TargetFramework;
    //            });

    //        this.solution = await this.workspace.OpenSolutionAsync(options.SlnFile.FullName, null, projLoadHandler, CancellationToken.None);
    //        foreach (var p in this.solution.Projects)
    //        {
    //            var comp = await p.GetCompilationAsync();
    //        }

    //        this.solutionGraph = this.solution.GetProjectDependencyGraph();
    //        var projectsInOptions = this.options.ProjectsToTest.Append(new FileInfoKey(this.options.PublishedProject))
    //            .Select(i =>
    //                this.solution.Projects.SingleOrDefault(p => new FileInfoKey(p.FilePath).Equals((i))))
    //            .Where(p => p != null)
    //            .ToArray();
    //        var referencedProjectIds =
    //            projectsInOptions.SelectMany(p => this.solutionGraph.GetProjectsThatThisProjectTransitivelyDependsOn(p.Id))
    //                .ToArray();

    //        this.allProjects = this.solution.Projects
    //            .Where(p => p.FilePath != null)
    //            .Where(p =>
    //                !this.options.OnlyLoadReferencedProjects
    //                || (projectsInOptions.Any(i => i.Id == p.Id) || referencedProjectIds.Contains(p.Id)))
    //            .ToDictionary(p => new FileInfoKey(p.FilePath), p => p);
    //        foreach (var projFile in this.options.ProjectsToTest.Append(new FileInfoKey(this.options.PublishedProject)))
    //        {
    //            if (!this.allProjects.ContainsKey(projFile))
    //            {
    //                this.allProjects.Add(projFile, await this.workspace.OpenProjectAsync(projFile.Info.FullName));
    //            }
    //        }
    //        this.idToProject = this.allProjects.Values.ToDictionary(p => p.Id, p => p);
    //    }

    //    public void Dispose()
    //    {
    //        this.output?.Dispose();
    //        this.workspace?.Dispose();
    //    }

    //    public async Task Header()
    //    {
    //        var framework = this.projectFrameworks.Values
    //            .Select(v => Regex.Replace(v, "^\\D+", string.Empty))
    //            .Distinct().ToArray();
    //        Array.Sort(framework);
    //        string version;
    //        if (framework.Length == 1)
    //        {
    //            version = framework.Single();
    //        }
    //        else
    //        {
    //            //TODO: multi fw
    //            version = framework.Last();
    //        }

    //        await this.output.WriteLineAsync("ARG OUTPUT_DIR=\"/output/\"");
    //        await this.output.WriteLineAsync($"ARG VERSION=\"{version}-buster\"");
    //        await this.output.WriteLineAsync("ARG ALT_SDK_VERSION=\"2.1.805\"");
    //        await this.output.WriteLineAsync("FROM mcr.microsoft.com/dotnet/core/aspnet:$VERSION-slim AS base");
    //        await this.output.WriteLineAsync("WORKDIR /app");
    //        await this.output.WriteLineAsync("EXPOSE 5000");
    //        await this.output.WriteLineAsync("USER 33");
    //        await this.output.WriteLineAsync("");
    //        await this.output.WriteLineAsync("FROM mcr.microsoft.com/dotnet/core/sdk:$VERSION AS deps");
    //        await this.output.WriteLineAsync("ARG PrivateNugetSource");
    //        await this.output.WriteLineAsync("ARG OUTPUT_DIR");
    //        await this.output.WriteLineAsync("");
    //    }

    //    public async Task Restore()
    //    {
    //        this.sortedProjects = this.solutionGraph.GetTopologicallySortedProjects().ToArray();
    //        var sortedProjectPaths = this.allProjects.Values.OrderBy(p => Array.IndexOf(this.sortedProjects, p.Id))
    //            .Select(p => Path.GetRelativePath(this.options.ContextDir.FullName, p.FilePath!))
    //            .ToArray();
    //        var dirs = await this.CreateDirs();
    //        await this.output.WriteLineAsync("WORKDIR /src");
    //        if (this.options.OnlyLoadReferencedProjects)
    //        {
    //            await this.CopyReferenced(sortedProjectPaths);
    //        }
    //        else
    //        {
    //            await this.CopyAll(dirs);
    //        }

    //        if (this.options.OnlyLoadReferencedProjects)
    //        {
    //            foreach (var path in sortedProjectPaths)
    //            {
    //                await this.output.WriteLineAsync($"RUN dotnet restore {convertPath(path, false)}");
    //            }
    //        }
    //        else
    //        {
    //            await this.output.WriteLineAsync($"RUN dotnet restore {this.options.SlnFile.Name}");
    //        }
    //        await this.output.WriteLineAsync("");
    //    }

    //    public async Task WriteBuildMain(string[] toCopy, string[] commonDependencies)
    //    {
    //        await this.output.WriteLineAsync("FROM deps AS build\n");
    //        foreach (var containerPath in toCopy.Except(commonDependencies))
    //        {
    //            await this.output.WriteLineAsync($"COPY {containerPath} {containerPath}");
    //        }
    //        await this.output.WriteLineAsync(
    //            "RUN dotnet build ActivityManager.Activity.Api/ActivityManager.Activity.Api.csproj -c Release --no-restore");
    //        await this.output.WriteLineAsync("\n");
    //    }

    //    public async Task<string[]> GetBuildMainData()
    //    {
    //        return await this.getDepsToCopy(new[] { this.options.PublishedProject });
    //    }

    //    private async Task<string[]> getDepsToCopy(FileInfo[] projects)
    //    {
    //        List<string> paths = new List<string>();
    //        foreach (var project in projects)
    //        {
    //            var proj = this.allProjects[new FileInfoKey(project)];
    //            //var directDeps = this.graph.GetProjectsThatThisProjectDirectlyDependsOn(proj.Id);
    //            var deps = this.solutionGraph.GetProjectsThatThisProjectTransitivelyDependsOn(proj.Id)
    //                .OrderBy(p =>
    //                {
    //                    int overallIndex = Array.IndexOf(this.sortedProjects, p);
    //                    return overallIndex;
    //                    //if (overallIndex > this.sortedProjects.Length / 2 && directDeps.Contains(p))
    //                    //{

    //                    //}
    //                });

    //            foreach (var id in deps.Append(proj.Id))
    //            {
    //                var path = this.solution.GetProject(id)?.FilePath;
    //                if (path == null)
    //                {
    //                    this.logger.Warning("# missing project " + id);
    //                    continue;
    //                }

    //                paths.Add(this.convertPath(Path.GetDirectoryName(path), true));
    //            }
    //        }
    //        return paths.ToArray();
    //    }

    //    private async Task<string[]> CreateDirs()
    //    {
    //        var projectDirs = this.allProjects.Keys.Select(f => f.Info.Directory?.Parent)
    //            .Where(d => d != null)
    //            .Select(d => this.convertPath(d.FullName, true))
    //            .Distinct()
    //            .Where(p => p.Trim('/') != ".")
    //            .Select(p => "/src/" + p)
    //            .ToArray();
    //        await this.output.WriteLineAsync($"RUN mkdir -p /src /test /output {string.Join(" ", projectDirs)}");
    //        return projectDirs;
    //    }

    //    private string convertPath(string input, bool makeRelative)
    //    {
    //        var path = makeRelative ? Path.GetRelativePath(this.options.ContextDir.FullName, input) : input;
    //        if (Path.DirectorySeparatorChar == '\\')
    //        {
    //            return path.Replace("\\", "/");
    //        }

    //        return path;
    //    }

    //    private async Task CopyAll(string[] dirs)
    //    {
    //        var sln = convertPath(this.options.SlnFile.FullName, true);
    //        await this.output.WriteLineAsync($"COPY {sln} */*.*proj ./");
    //        foreach (var d in dirs)
    //        {
    //            await this.output.WriteLineAsync($"COPY {d}/*/*.*proj ./{d}/");
    //        }

    //        var quotedDirs = string.Join(" ", dirs.Select(d => $"\"{d}\""));
    //        await this.output.WriteLineAsync($"RUN for d in \".\" {quotedDirs}; do \\");
    //        await this.output.WriteLineAsync("for p in $(ls $d/*.*proj); do \\");
    //        await this.output.WriteLineAsync("  mkdir ${p%.*} -p && \\");
    //        await this.output.WriteLineAsync("  mv $p ${p%.*}/ ; \\");
    //        await this.output.WriteLineAsync(" done \\");
    //        await this.output.WriteLineAsync("done");
    //        await this.output.WriteLineAsync("\n");
    //    }

    //    private async Task CopyReferenced(IEnumerable<string> sortedProjectPaths)
    //    {
    //        foreach (var path in sortedProjectPaths)
    //        {
    //            await this.output.WriteLineAsync($"COPY {convertPath(path, false)} {convertPath(Path.GetDirectoryName(path), false)}");
    //        }
    //        await this.output.WriteLineAsync("");
    //    }

    //    public async Task<ProjectDeps[]> GetRunTestsData()
    //    {
    //        // This horribly inefficient - but I'm in a hurry
    //        var projects = this.options.ProjectsToTest
    //            .Select(p => this.allProjects[(p)])
    //         .Select(p => new
    //         {
    //             Project = p,
    //             Deps = this.solutionGraph.GetProjectsThatThisProjectTransitivelyDependsOn(p.Id)
    //                .Select(d => this.solution.GetProject(d)).ToArray(),
    //             TestDeps = new List<ProjectId>()
    //         }).ToArray();
    //        Dictionary<ProjectId, ProjectDeps> projectDeps = new Dictionary<ProjectId, ProjectDeps>();
    //        foreach (var project in projects)
    //        {
    //            var deps = projects
    //                .Where(p => p.Project.Id != project.Project.Id)
    //                .SelectMany(p =>
    //                project.Deps.Select(i => i.Id).Intersect(p.Deps.Select(i => i.Id)).Select(d => new
    //                {
    //                    ProjectId = d.Id,
    //                    ReferencedBy = p.Project,
    //                    IsTestedByOtherProj = isTestedByProject(idToProject[d], p.Project, project.Project),
    //                    IsTestedByCurrentProj = isTestedByProject(idToProject[d], project.Project, p.Project),
    //                }))
    //                .GroupBy(i => i.ReferencedBy.Id)
    //                .Where(p => p.Any(d => d.IsTestedByOtherProj))
    //                .ToArray();

    //            projectDeps.Add(project.Project.Id, new ProjectDeps
    //            {
    //                Project = project.Project,
    //                TestDeps = deps.Where(d => d.All(i => !i.IsTestedByCurrentProj)).Select(x => x.Key).ToArray(),
    //                ProjectFileDependencies = await getDepsToCopy(new[] { new FileInfo(project.Project.FilePath) })
    //            });
    //        }

    //        foreach (var project in projectDeps.Values)
    //        {
    //            var dependenciesOfDependencies = project.TestDeps.SelectMany(i => projectDeps[i].TestDeps)
    //                .ToHashSet();
    //            project.TestDeps = project.TestDeps.Where(i => !dependenciesOfDependencies.Contains(i)).ToArray();
    //        }

    //        var graph = new AdjacencyGraph<ProjectDeps, Edge<ProjectDeps>>();
    //        foreach (var key in projectDeps.Keys)
    //        {
    //            var p = projectDeps[key];
    //            graph.AddVertex(p);
    //            foreach (var dep in p.TestDeps)
    //            {
    //                graph.AddEdge(new Edge<ProjectDeps>(p, projectDeps[dep]));
    //            }
    //        }

    //        return graph.TopologicalSort().Reverse().ToArray();
    //    }

    //    private bool isTestedByProject(Project reference, Project project, Project otherProject)
    //    {
    //        var refName = reference.Name.ToLower();
    //        var projectName = project.Name.ToLower();
    //        var otherProjectName = otherProject.Name.ToLower();

    //        return (projectName.Contains(refName) && !otherProjectName.Contains(refName)
    //                || (Regex.Replace(projectName, "\\..*?tests?", string.Empty) == refName &&
    //                    Regex.Replace(otherProjectName, "\\..*?tests?", string.Empty) != refName));
    //    }

    //    public async Task<string[]> CopyCommon(string[][] dependencies)
    //    {
    //        var toCopy = dependencies.First();
    //        foreach (var dependency in dependencies.Skip(1))
    //        {
    //            toCopy = toCopy.Intersect(dependency).ToArray();
    //        }
    //        foreach (var containerPath in toCopy)
    //        {
    //            await this.output.WriteLineAsync($"COPY {containerPath} {containerPath}");
    //        }

    //        return toCopy;
    //    }

    //    public async Task WriteTests(ProjectDeps[] testProjects, string[] commonDependencies)
    //    {
    //        foreach (var project in testProjects)
    //        {
    //            await this.output.WriteLineAsync("FROM deps AS test_" + project.Project.Name);
    //            await this.output.WriteLineAsync("");
    //            var deps = project.ProjectFileDependencies.Except(commonDependencies);
    //            foreach (var containerPath in deps)
    //            {
    //                await this.output.WriteLineAsync($"COPY {containerPath} {containerPath}");
    //            }

    //            await this.output.WriteLineAsync($"\nRUN dotnet build {project.Project.Name} --no-restore");
    //            await this.output.WriteLineAsync("");
    //            foreach (var projectId in project.TestDeps)
    //            {
    //                var testDep = this.idToProject[projectId];
    //                await this.output.WriteLineAsync("COPY --from=test_" + testDep.Name + " /test/ /test/");
    //            }

    //            var projDir = convertPath(Path.GetDirectoryName(project.Project.FilePath), true);
    //            var projFileInDir = convertPath(project.Project.FilePath, true);
    //            projFileInDir =Path.GetRelativePath(projDir, projFileInDir);
    //            var projFw = this.projectFrameworks[new FileInfoKey(project.Project.FilePath)];

    //            await this.output.WriteLineAsync("");
    //            string args = $"test {projFileInDir} --no-build /clp:ForceConsoleColor --filter 'Category!=Integration' --results-directory '/test'";
    //            await this.output.WriteLineAsync($"RUN cd {projDir}; /root/.dotnet/tools/coverlet bin/Debug/{projFw}/{project.Project.AssemblyName}.dll --target \"dotnet\" --targetargs \"{args}\" \\");

    //            bool firstDep = true;
    //            var projectNotReferenced = testProjects.All(p => !p.TestDeps.Contains(project.Project.Id));
    //            if (projectNotReferenced)
    //            {
    //                await this.output.WriteAsync("  --format cobertura ");
    //            }
    //            foreach (var dep in project.TestDeps.Select(i => this.idToProject[i]))
    //            {
    //                await this.output.WriteLineAsync(
    //                    $"{(firstDep ? String.Empty : "//")}  --merge-with /test/{dep.Name}.json \\");
    //                firstDep = false;
    //            }

    //            if (projectNotReferenced)
    //            {
    //                await this.output.WriteAsync($"  && cp coverage.xml /test/{project.Project.Name}.cobertura.xml");
    //            }
    //            else
    //            {
    //                await this.output.WriteAsync($"  && cp coverage.json /test/{project.Project.Name}.json");
    //            }

    //            await this.output.WriteLineAsync(" || touch /test/fail_build");
    //            await this.output.WriteLineAsync("");
    //        }
    //    }

    //}

    //public class ProjectDeps
    //{
    //    public Project Project { get; set; }
    //    public ProjectId[] TestDeps { get; set; }
    //    public string[] ProjectFileDependencies { get; set; }
    //}
}
