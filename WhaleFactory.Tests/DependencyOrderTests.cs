using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace WhaleFactory.Tests
{
    public class DependencyOrderTests : IDisposable
    {
        private readonly TestData testData;
        private Logger logger;

        public DependencyOrderTests(ITestOutputHelper output)
        {
            this.logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.TestOutput(output, LogEventLevel.Debug).CreateLogger(); 
            this.testData = new TestData(this.logger);
        }
        [InlineData(DependencySortOrder.TopologicalSolutionWide)]
        //[InlineData(DependencySortOrder.TopologicalByProject)]
        [Theory]
        public void ShouldOrderCommonDepsTopologicalBySolution(DependencySortOrder sortMode)
        {
            int projectCount = 10;
            var ws = testData.GetLinearSolution(projectCount);
            var before = ws.CurrentSolution.Projects.Select(p => p.Id).ToArray();
            var sln = testData.AddRandomReferences(ws).CurrentSolution;
            sln.Projects.Select(p => p.Id).ToArray().Should().Equal(before);
            var projects = sln.Projects.ToDictionary(k => new FileInfoKey(k.FilePath), v => v);

            var dependencyOrder = new DependencyOrder(sln, sln.GetProjectDependencyGraph(), projects, sortMode, this.logger);

            var sorted = dependencyOrder.AllSortedProjectIds;
            sorted.Should().NotEqual(before);

            var result = sorted.Select(i => sln.GetProject(i)).ToArray();
            var passed = new HashSet<string>();
            int references = 0;
            foreach (var r in result)
            {
                var refedProjectNames = r.ProjectReferences.Select(p => sln.GetProject(p.ProjectId)?.Name).ToArray();
                foreach (var name in refedProjectNames)
                {
                    Assert.Contains(name, passed);
                    references++;
                }

                passed.Add(r.Name);
            }
        }

        [InlineData(true)]
        [InlineData(false)]
        [Theory]
        public void ShouldOrderCommonDepsByDate(bool useGitDate)
        {
            int projectCount = 100;
            var ws = testData.GetLinearSolution(projectCount);
            if (useGitDate)
            {
                void Git(string args)
                {
                    var p = Process.Start(new ProcessStartInfo("git", args) {WorkingDirectory = this.testData.Root,RedirectStandardOutput = true,RedirectStandardError = true});
                    p.WaitForExit();
                    this.logger.Debug(p.StandardError.ReadToEnd()+ p.StandardError.ReadToEnd());
                }

                Git("init");
                Git("add *5*");
                Git($"commit -m past --date \"{DateTime.Now.AddHours(-1):s}\"");
                Git("add .");
                Git("commit -m present");
            }
            
            var before = ws.CurrentSolution.Projects.Select(p => p.Id).ToArray();
            var sln = testData.AddRandomReferences(ws).CurrentSolution;
            sln.Projects.Select(p => p.Id).ToArray().Should().Equal(before);
            var projects = sln.Projects.ToDictionary(k => new FileInfoKey(k.FilePath), v => v);
            Thread.Sleep(1000);
            foreach (var directoryInfo in projects
                .Where(p => p.Value.Name.Contains("3"))
                .Select(p => p.Key.Info.Directory).Distinct())
            {
                directoryInfo.LastWriteTimeUtc = DateTime.UtcNow.AddSeconds(10);
            }

            var dependencyOrder = new DependencyOrder(sln, sln.GetProjectDependencyGraph(), projects, DependencySortOrder.DateModified, this.logger);

            var sorted = dependencyOrder.AllSortedProjectIds;
            sorted.Should().NotEqual(before);
            var result = sorted.Select(i => sln.GetProject(i)).ToArray();
            if (!useGitDate)
            {
                foreach (var p in result.Skip(projectCount - 10))
                {
                    p.Name.Should().Contain("3");
                }
            }
            else
            {
                Assert.Equal(useGitDate, result.Take(10).All(p => p != null && p.Name.Contains("5")));
            }
            result.Select(p => DependencyOrder.GetProjectTime(p.FilePath, this.logger)).Should().BeInAscendingOrder();
        }

        //[Theory]
        //[InlineData(DependencySortOrder.TopologicalSolutionWide,false)]
        //[InlineData(DependencySortOrder.DateModified,false)]
        //[InlineData(DependencySortOrder.TopologicalSolutionWide,true)]
        //[InlineData(DependencySortOrder.DateModified,true)]
        //public void ShouldOrderProjectDeps(DependencySortOrder order, bool all, int start = 0)
        //{
        //    int projectCount = 100;
        //    var ws = testData.GetLinearSolution(projectCount);
            
        //    //ws = testData.AddRandomReferences(ws, 1, 50, 50);
        //    var notReferenced = ws.CurrentSolution.Projects.Select(p => p.Id).ToArray();
        //    ws =this.testData.ClearReferences(ws, notReferenced);
        //    //var sln = this.testData.AddReferencesWithFunc(ws, 0, i =>
        //    //        i + 1 == 9 ? new[] {i, i + 1, i + 1} : (i + 2 < 10 ? new[] {i, i + 1, i + 2, i + 1} : new int[0]))
        //    //    .CurrentSolution;
        //    var sln = this.testData.MakeTree(ws, 50)
        //        .CurrentSolution;

        //    var projects = sln.Projects.ToDictionary(k => new FileInfoKey(k.FilePath), v => v);
        //    var dependencyOrder = new DependencyOrder(sln, sln.GetProjectDependencyGraph(), projects, order, this.logger);
        //    var projectRefs = dependencyOrder.GetProjectReferences(sln.ProjectIds[start], sln, all);
        //    var pids = sln.ProjectIds.ToArray();
        //    var pidIndexes= projectRefs.Select(i => Array.IndexOf(pids, i)).ToArray();
        //    this.logger.Debug(string.Join(" ",pidIndexes));
        //    if (all)
        //    {
        //        pidIndexes.Should().BeEquivalentTo(Enumerable.Range(1,9));
        //    }
        //    else
        //    {
        //        pidIndexes.Should().BeEquivalentTo(Enumerable.Range(1,2));
        //    }

        //    //if (order == DependencySortOrder.DateModified || order == DependencySortOrder.TopologicalSolutionWide)
        //    //{
        //        pidIndexes.Should().Equal(dependencyOrder.AllSortedProjectIds.ToList().Where(p=>projectRefs.Contains(p)).Select(i => Array.IndexOf(pids, i)));
        //    //}

        //    //if (order == DependencySortOrder.TopologicalByProject && all)
        //    //{
        //    //    pidIndexes.Should().Equal(new[] { 9,8 ,7 ,6, 5 ,4 ,3, 2 ,1 });
        //    //}
        //}

        public void Dispose()
        {
            this.testData?.Dispose();
        }
    }
}
