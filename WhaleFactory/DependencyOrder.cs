using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Serilog;

namespace WhaleFactory
{
    public class DependencyOrder
    {
        private readonly Solution solution;
        private readonly ProjectDependencyGraph solutionGraph;

        public DependencyOrder(Solution solution, ProjectDependencyGraph solutionGraph,
            Dictionary<FileInfoKey, Project> allProjects, DependencySortOrder sortOrder, ILogger logger)
        {
            this.solution = solution;
            this.solutionGraph = solutionGraph;
            this.sortOrder = sortOrder;
            if (sortOrder == DependencySortOrder.DateModified)
            {
                this.AllSortedProjectIds = this.solution.Projects
                    .OrderBy(p => GetProjectTime(p.FilePath, logger)).Select(p => p.Id)
                    .ToArray();
                //this.AllSortedProjectIds = this.solution.Projects
                //    .GroupBy(p => Path.GetDirectoryName(p.FilePath))
                //    .SelectMany(g => g.Count()>1 ? g.SelectMany(i => (dir) g.AsEnumerable())
                //    .ToArray());
            }
            else
            {
                this.AllSortedProjectIds = this.solutionGraph.GetTopologicallySortedProjects()//.Reverse()
                                                                                              .ToArray();
            }

            this.idToProject = allProjects.Values.ToDictionary(p => p.Id, p => p);
        }

        public static DateTime GetProjectTime(string project, ILogger logger)
        {
            var projectDir = new FileInfo(project).Directory;

            var gitDir = Utils.GetGitDir(projectDir);
            try
            {
                if (gitDir?.Exists == true && gitDir.Parent!=null)
                {
                    string gitRoot = gitDir.Parent.FullName;
                    string args = $"--no-pager --work-tree {gitRoot} --git-dir {gitDir.FullName} log --date=iso -1  -- {Path.GetRelativePath(gitRoot, projectDir?.FullName!)}";
                    logger.Debug("git "+args);
                    var p = Process.Start(new ProcessStartInfo("git",args)
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    });
                    p.WaitForExit(10000);
                    string output = p.StandardOutput.ReadToEnd();
                    logger.Debug(output);
                    logger.Debug(p.StandardError.ReadToEnd());
                    if (p.ExitCode == 0)
                    {
                        
                        var m = Regex.Match(output, @"^Date:\s*(\d.*)$",RegexOptions.Multiline);
                        if (m.Success)
                        {
                            return DateTime.Parse(m.Groups[1].Value);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warning("Getting date for: " + projectDir + " " + ex.Message);
            }

            return Directory.GetLastWriteTime(projectDir?.FullName!);
        }

        private Dictionary<ProjectId, Project> idToProject;
        private readonly DependencySortOrder sortOrder;

        public ProjectId[] AllSortedProjectIds { get; set; }

        public ProjectId[] GetProjectReferences(ProjectId id, Solution sln, bool all)
        {
            //var proj = sln.GetProject(id);
            //if (proj==null){throw new InvalidOperationException("Cant find project" +id);}

            IEnumerable<ProjectId> references;
            if (all)
            {
                references= this.solutionGraph.GetProjectsThatThisProjectTransitivelyDependsOn(id);
            }else{
                references = this.solutionGraph.GetProjectsThatThisProjectDirectlyDependsOn(id);
            }

            //if (this.sortOrder == DependencySortOrder.DateModified ||
            //    this.sortOrder == DependencySortOrder.TopologicalSolutionWide)
            //{
                return references.OrderBy(i => Array.IndexOf(this.AllSortedProjectIds, i)).ToArray();
            //}

            //if (!all)
            //{
            //    return references.ToArray();
            //}

            //var outOfOrder = references.ToHashSet();
            //references=new List<ProjectId>();
            //throw new NotImplementedException();
        }

        public IEnumerable<Project> OrderProjects(IEnumerable<Project> projects) =>
            projects.OrderBy(p => this.GetOrderedProjectIndex(p.Id.Id));

        public int GetOrderedProjectIndex(Guid id)
            => Array.IndexOf(this.AllSortedProjectIds, id);
    }

    public enum DependencySortOrder
    {
        TopologicalSolutionWide, //TODO: GetTopologicallySortedProjects is not deterministic - replace
        DateModified
    }
}