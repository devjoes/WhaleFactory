using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Serilog;

namespace WhaleFactory
{
    internal class TestProvider
    {
        private readonly ILogger logger;
        private readonly FileInfo[] testProjects;
        private readonly string findTestsExcluding;
        private readonly int findTestsDepth;

        public TestProvider(ILogger logger, IEnumerable<FileInfo> testProjects, string findTestsExcluding, in int findTestsDepth)
        {
            this.logger = logger;
            this.testProjects = testProjects.ToArray();
            this.findTestsExcluding = findTestsExcluding;
            this.findTestsDepth = findTestsDepth;
        }

        public FileInfo[] GetTests(Solution solution, FileInfo publishedProject)
        {
            var projectsToTest = this.testProjects.ToList();
            if (this.findTestsDepth>-1)
            {
                var rxExclude = this.findTestsExcluding == null ? null : new Regex(this.findTestsExcluding);
                var projects = this.TestsForProjects(solution,new []{publishedProject}, rxExclude, this.findTestsDepth);
                foreach (var p in projects)
                {
                    projectsToTest.Add(new FileInfo(p.FilePath));
                }
            }

            return projectsToTest.ToArray();
        }


        private Project[] TestsForProjects(Solution solution, FileInfo[] projectFiles, Regex excludeProjects, int searchDepth)
        {
            var projects = solution.Projects.Where(p => projectFiles.Any(f => f.FullName.Equals(p.FilePath, StringComparison.InvariantCultureIgnoreCase))).ToArray();
            var msg = "Found projects: " + string.Join(", ", projects.Select(p => p.Name));
            if (projects.Length != projectFiles.Length)
            {
                this.logger.Warning("Only " + msg);
            }
            else
            {
                this.logger.Information(msg);
            }

            var solutionGraph = solution.GetProjectDependencyGraph();

            var testProjects = solution.Projects.Where(p => p.MetadataReferences.Any(r =>
                    r.Display != null && r.Display.Contains("microsoft.testplatform.core",
                        StringComparison.InvariantCultureIgnoreCase)))
                .ToDictionary(k => k.Id, v => v);
            var projectsCoveredByTests = testProjects.Values.SelectMany(t =>
                    solutionGraph.GetProjectsThatThisProjectTransitivelyDependsOn(t.Id)
                        .Select(p => new ProjectId[] { p, t.Id }))
                .GroupBy(a => a[0])
                .ToDictionary(
                    k => k.Key,
                    v => v.Select(i => i[1]).ToArray());


            var processed = new HashSet<ProjectId>();
            var testsFound = new HashSet<ProjectId>();
            var todo = new Queue<(ProjectId Id, int Depth)>(projects.Select(p => (p.Id, 1)));

            while (todo.TryDequeue(out var work))
            {
                var (projectId, depth) = work;
                this.FindTests(excludeProjects, this.logger, projectsCoveredByTests, projectId, testsFound, solution, testProjects, depth);
                var children = solutionGraph.GetProjectsThatThisProjectDirectlyDependsOn(projectId);
                foreach (var childId in children)
                {
                    if (processed.Contains(childId))
                    {
                        continue;
                    }

                    processed.Add(childId);
                    if (depth < searchDepth || searchDepth == 0)
                    {
                        todo.Enqueue((childId, depth + 1));
                    }
                }
            }

            return testsFound.Select(i => testProjects[i]).ToArray();
        }

        private void FindTests(Regex excludeProjects, ILogger logger, Dictionary<ProjectId, ProjectId[]> projectsCoveredByTests,
            ProjectId childId, HashSet<ProjectId> testsFound, Solution solution, Dictionary<ProjectId, Project> testProjects, int depth)
        {
            if (!projectsCoveredByTests.ContainsKey(childId))
            {
                return;
            }

            var testedBy = projectsCoveredByTests[childId];
            var testProjsToProcess = testedBy.Except(testsFound).ToArray();

            Project childProject;
            if (!testProjsToProcess.Any() || (childProject = solution.GetProject(childId)) == default || (excludeProjects != null && excludeProjects.IsMatch(childProject.Name)))
            {
                return;
            }

            foreach (var testProjectId in testProjsToProcess)
            {
                testsFound.Add(testProjectId);
                var testProj = testProjects[testProjectId];
                logger.Information($"Found {testProj.Name} at level {depth + 1}. Initially found in {childProject?.Name}");
            }
        }

    }
}