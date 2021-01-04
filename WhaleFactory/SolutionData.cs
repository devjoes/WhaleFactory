using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Serilog;

namespace WhaleFactory
{
    //TODO: activitymanager-backend/Reporting - edge cases
    public class SolutionData : IDisposable
    {
        private CmdOptions options;
        private readonly ILogger logger;
        private MSBuildWorkspace workspace;
        private Solution solution;
        private Dictionary<FileInfoKey, Project> allProjects;
        private Dictionary<FileInfoKey, string> projectFrameworks;
        private ProjectDependencyGraph solutionGraph;

        private FileInfo solutionFile;
        private FileInfoKey[] projectsToTest;

        static SolutionData()
        {
            MSBuildLocator.RegisterDefaults();
        }

        public SolutionData(FileInfo solutionFile, ILogger logger)
        {
            this.solutionFile = solutionFile;
            this.logger = logger;
            this.workspace = MSBuildWorkspace.Create();
        }

        public async Task Init()
        {
            this.projectFrameworks = new Dictionary<FileInfoKey, string>();
            if (this.solution != null)
            {
                throw new InvalidOperationException();
            }

            this.workspace.WorkspaceFailed += (sender, args) =>
            {
                if (args.Diagnostic.Kind == WorkspaceDiagnosticKind.Warning)
                {
                    this.logger.Warning(args.Diagnostic.Message);
                }
                else
                {
                    this.logger.Error(args.Diagnostic.Message);
                }
            };

            var projLoadHandler = new Progress<ProjectLoadProgress>(
                progress => { this.projectFrameworks[new FileInfoKey(progress.FilePath)] = progress.TargetFramework; });

            this.solution = await this.workspace.OpenSolutionAsync(this.solutionFile.FullName, null,
                projLoadHandler, CancellationToken.None);

            this.solutionGraph = this.solution.GetProjectDependencyGraph();
        }

        public async Task Load(CmdOptions dockerFileOptions)
        {
            if (this.options != null || dockerFileOptions.SlnFile != this.solutionFile)
            {
                throw new InvalidOperationException();
            }

            this.options = dockerFileOptions;

            Project[] projectsInOptions;
            (this.projectsToTest, projectsInOptions) = SolutionData.GetProjectsFromOptions(this.options, this.solution);
            
            this.allProjects = GetAllProjectsByFile(projectsInOptions, this.solution, this.options.OnlyLoadReferencedProjects);

            //foreach (var projFile in projectFilesToTest.Append(new FileInfoKey(this.options.PublishedProject)))
            //{
            //    if (!this.allProjects.ContainsKey(projFile))
            //    {
            //        this.allProjects.Add(projFile, await this.workspace.OpenProjectAsync(projFile.Info.FullName));
            //    }
            //}
            this.dependencyOrdering = new DependencyOrder(this.solution, this.solutionGraph, this.allProjects, this.options.DependencyOrder, this.logger);
        }

        public static (FileInfoKey[] projectFilesToTest, Project[] projectsInOptions) GetProjectsFromOptions(CmdOptions options, Solution solution)
        {
            var projectFilesToTest = options.ProjectsToTest
                .Where(f => solution.Projects.Any(p => p.FilePath != null && p.FilePath.Equals(f.FullName)))
                .Select(f => new FileInfoKey(f)).ToArray();
            var projectsInOptions = projectFilesToTest.Append(new FileInfoKey(options.PublishedProject))
                    .Select(i =>
                        solution.Projects.SingleOrDefault(p => new FileInfoKey(p.FilePath).Equals(i)))
                    .Where(p => p != null).ToArray();
            return (projectFilesToTest, projectsInOptions);
        }


        public static Dictionary<FileInfoKey, Project> GetAllProjectsByFile(Project[] projectsInOptions, Solution solution, bool onlyLoadReferencedProjects)
        {
            var solutionGraph = solution.GetProjectDependencyGraph();
            var referencedProjectIds = projectsInOptions.SelectMany(p => solutionGraph.GetProjectsThatThisProjectTransitivelyDependsOn(p.Id))
                .ToArray();
            return solution.Projects
                .Where(p => p.FilePath != null)
                .Where(p =>
                    !onlyLoadReferencedProjects
                    || (projectsInOptions.Any(i => i.Id == p.Id) || referencedProjectIds.Contains(p.Id)))
                .GroupBy(p=>p.Id).Select(g => g.First())
                .ToDictionary(p => new FileInfoKey(p.FilePath), p => p);
        }

        public void Dispose()
        {
            this.workspace?.Dispose();
        }

        public DockerData GetDockerData()
       => new DockerData(this.getProjFiles, this.getDirs, this.getTests, this.getMainProject)
       {
           OnlyRestoreReferencedProjects = this.options.OnlyLoadReferencedProjects,
           Template = this.options.Template,
           SolutionName = this.options.SlnFile.Name,
           SolutionPath = this.convertPath(this.options.SlnFile.FullName, true),
           Frameworks = this.getFrameworks(),
           CommonDepsThreshold = this.options.CommonDepsThreshold,
           DependencyOrdering = this.dependencyOrdering
       };

        private FrameworkData getFrameworks()
        {
            var all = this.projectFrameworks.Values
                .Where(f => !f.Contains("standard")) //TODO: Improve this logic and map from standard to core fw
                .Select(v => Regex.Replace(v, "^\\D+", string.Empty))
                .Distinct().OrderByDescending(i => i).ToArray();
            var majors = all.GroupBy(f => f.Split(".").First())
                .Select(g => g.Max())
                .OrderByDescending(i => i).ToArray();
            return new FrameworkData { All = all, Majors = majors };
        }

        private ImmutableArray<string> getProjFiles() =>this.dependencyOrdering.OrderProjects(this.allProjects.Values)
            .Select(p => Path.GetRelativePath(this.options.ContextDir.FullName, p.FilePath!))
            .ToImmutableArray();

        private DockerProjectData getMainProject()
        {
            var p = this.allProjects[new FileInfoKey(this.options.PublishedProject)];
            return new DockerProjectData(p, this.options.ContextDir, () => this.getDependencies(p));
        }

        private ImmutableArray<string> getDirs() =>
            this.allProjects
                .OrderBy(p => Array.IndexOf(this.dependencyOrdering.AllSortedProjectIds, p.Value.Id))
                .Select(f => f.Key.Info.Directory?.Parent)
                .Where(d => d != null)
                .Select(d => this.convertPath(d.FullName, true))
                .Distinct()
                .Where(p => p.Trim('/') != ".")
                .ToImmutableArray();

        private ImmutableArray<DockerProjectData> getTests()
        => this.projectsToTest
                .Select(p => this.allProjects[(p)])
                .OrderBy(p => Array.IndexOf(this.dependencyOrdering.AllSortedProjectIds, p.Id))
                .Select(p => new DockerProjectData(p, this.options.ContextDir, () => this.getDependencies(p)))
                .ToImmutableArray();

        private readonly Dictionary<ProjectId, ImmutableArray<DockerProjectData>> previouslyFound = new Dictionary<ProjectId, ImmutableArray<DockerProjectData>>();
        private DependencyOrder dependencyOrdering;

        private ImmutableArray<DockerProjectData> getDependencies(Project project)
        {
            if (!this.previouslyFound.ContainsKey(project.Id))
            {
                this.previouslyFound.Add(project.Id,
                this.solutionGraph.GetProjectsThatThisProjectTransitivelyDependsOn(project.Id)
                    .Select(d => this.solution.GetProject(d))
                    .OrderByDescending(p => Array.IndexOf(this.dependencyOrdering.AllSortedProjectIds, p.Id))
                    .Select(p => new DockerProjectData(p, this.options.ContextDir, () => this.getDependencies(p)))
                    .ToImmutableArray());
            }

            return this.previouslyFound[project.Id];
        }

        private string convertPath(string input, bool makeRelative)
        => Utils.ConvertPath(input, makeRelative, this.options.ContextDir);



        //private bool isTestedByProject(Project reference, Project project, Project otherProject)
        //{
        //    var refName = reference.Name.ToLower();
        //    var projectName = project.Name.ToLower();
        //    var otherProjectName = otherProject.Name.ToLower();

        //    return (projectName.Contains(refName) && !otherProjectName.Contains(refName)
        //            || (Regex.Replace(projectName, "\\..*?tests?", string.Empty) == refName &&
        //                Regex.Replace(otherProjectName, "\\..*?tests?", string.Empty) != refName));
        //}

        public Solution GetSolution() => this.solution;
    }


    public class FrameworkData
    {
        public string[] Majors { get; set; }
        public string[] All { get; set; }
    }

    internal class Utils
    {
        public static string ConvertPath(string input, in bool makeRelative, DirectoryInfo context)
        {
            var path = makeRelative ? Path.GetRelativePath(context.FullName, input) : input;
            if (Path.DirectorySeparatorChar == '\\')
            {
                return path.Replace("\\", "/");
            }

            return path;
        }

        public static DirectoryInfo GetGitDir(DirectoryInfo dir) =>
            dir == null ? null : dir.GetDirectories(".git", SearchOption.TopDirectoryOnly).SingleOrDefault() ?? GetGitDir(dir.Parent);
    }
}
