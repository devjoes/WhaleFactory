using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.MSBuild;
using Xunit;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace WhaleFactory.Tests
{
    public class SolutionDataTests:IDisposable
    {
        private TestData testData;
        private Logger logger;

        public SolutionDataTests(ITestOutputHelper output)
        {
            this.logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.TestOutput(output, LogEventLevel.Debug)
                .CreateLogger();
            this.testData = new TestData(this.logger);
        }
        [Fact]
        public void ShouldReturnProjectsFromOptionsIfInSln()
        {
            var sln = testData.GetLinearSolution(5).CurrentSolution;
            var projFiles = sln.Projects.Select(p => new FileInfo(p.FilePath)).ToArray();
            var (files, projects) = SolutionData.GetProjectsFromOptions(new CmdOptions
                {PublishedProject = projFiles.First(), ProjectsToTest = projFiles.Skip(1).Append(new FileInfo("notinsolution.csproj"))}, sln);


            projects.Select(p => p.Name).Should().BeEquivalentTo(sln.Projects.Select(f => f.Name));
            files.Select(f => f.Info.Name).Should().BeEquivalentTo(projFiles.Skip(1).Select(f => f.Name));
     
                
        }

        [Theory]
        [InlineData(0, 1, 0)]
        [InlineData(1, 1, 1)]
        [InlineData(2, 3, 2)]
        public void ShouldGetAllReferencedProjects(int skip, int take, int resultStart)
        {
            var sln = testData.GetLinearSolution(5).CurrentSolution;

            var allProjects = SolutionData.GetAllProjectsByFile(sln.Projects.Reverse().Skip(skip).Take(take).ToArray(), sln, true);

            allProjects.Select(p => p.Value.Name)
                .ShouldBeEquivalentTo(sln.Projects.Reverse().Skip(resultStart).Select(p => p.Name));
        }

        [Fact]
        public void ShouldGetAllProjects()
        {
            var sln = testData.GetLinearSolution(5).CurrentSolution;

            var allProjects = SolutionData.GetAllProjectsByFile(new Project[0], sln, false);

            allProjects.Select(p => p.Value.Name)
                .ShouldBeEquivalentTo(sln.Projects.Reverse().Select(p => p.Name));
            allProjects.Select(p => p.Key.Info.FullName).ShouldAllBeEquivalentTo(sln.Projects.Select(p=>p.FilePath));
        }

        public void Dispose()
        {
            this.testData?.Dispose();
        }
    }

}
