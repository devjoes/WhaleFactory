using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Serilog;
using Xunit;

namespace WhaleFactory.Tests
{
    public class TestData : IDisposable
    {
        private readonly ILogger logger;
        private readonly string tmp;

        public TestData(ILogger logger)
        {
            this.logger = logger;
            this.tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(this.tmp);
        }

        public string Root => this.tmp;

        private ProjectInfo addProject(AdhocWorkspace ws, string name, ProjectId reference = null, int modTimeSecsOffset = 0)
        {
            //TODO: test proj name and dir being different
            var dir = Path.Combine(this.tmp, name);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var csProj = new FileInfo(Path.Combine(dir, name + ".csproj"));
            csProj.Create().Dispose();
            csProj.LastWriteTimeUtc = DateTime.UtcNow.AddSeconds(modTimeSecsOffset);
            var project = ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), name,
                name, LanguageNames.CSharp, filePath: csProj.FullName);
            Assert.True(ws.TryApplyChanges(ws.CurrentSolution.AddProject(project)));
            if (reference != null)
            {
                Assert.True(ws.TryApplyChanges(ws.CurrentSolution.AddProjectReference(project.Id, new ProjectReference(reference))));
            }

            return project;
        }

        public AdhocWorkspace ClearReferences(AdhocWorkspace ws, ProjectId[] ids = null)
        {
            var solution = ws.CurrentSolution;

            foreach (var id in ws.CurrentSolution.Projects.Select(p => p.Id))
            {
                bool ShouldDel(ProjectId i) => ids == null || ids.Contains(id) || ids.Contains(i);
                while ((bool)ws.CurrentSolution.GetProject(id)?.ProjectReferences?.Select(p => p.ProjectId).Any(ShouldDel))
                {
                    var p = ws.CurrentSolution.GetProject(id);
                    Assert.True(solution.Workspace.TryApplyChanges(
                        solution.RemoveProjectReference(p.Id, p.ProjectReferences.First(r => ShouldDel(r.ProjectId)))));
                    solution = ws.CurrentSolution;
                }
            }

            return ws;
        }

        public AdhocWorkspace MakeTree(AdhocWorkspace ws, int count) =>
            this.AddReferencesWithFunc(ws, 0, i =>
            {
                List<int> result = new List<int>();
                if (i + 1 < count)
                {
                    result.Add(i + 1); // parent
                }

                if (i + i + 1 < count)
                {
                    result.Insert(0, i + i + 1); // right child
                }

                if (i + i < count)
                {
                    result.Insert(0, i + i); // left child
                }

                if (result.Count > 1)
                {
                    result.Insert(0, i); // next node
                }

                return result.ToArray();

            });

        public AdhocWorkspace AddRandomReferences(AdhocWorkspace ws, int depth = 1, int count = 0, int start = 0)
        {
            var solution = ws.CurrentSolution;
            if (count == 0)
            {
                count = solution.Projects.Count();
            }
            var projectsToMod = solution.ProjectIds.Skip(start).Take(count).ToArray();
            solution = ClearReferences(ws, projectsToMod).CurrentSolution;

            HashSet<int>[] parentRefs =
                Enumerable.Range(0, count).Select(i => new HashSet<int>(new[] { i })).ToArray();
            HashSet<int>[] refs =
                Enumerable.Range(0, count).Select(i => new HashSet<int>()).ToArray();
            var full = new List<int>();
            Random rnd = new Random(42);

            int iteration = 0;
            List<string> debug = new List<string>();

            int[] incomplete = Enumerable.Range(0, count).ToArray();
            do
            {
                iteration++;
                var from = incomplete[rnd.Next(0, incomplete.Length - 1)];
                //var allParentRefs = parentRefs.SelectMany(i => i).ToHashSet();
                var excludingProjectsReferencedByParents = incomplete.Except(parentRefs[from]);
                var alreadyReferenced = parentRefs[@from].Append(@from).Concat(refs[@from]).ToHashSet();
                var possibleTos = excludingProjectsReferencedByParents.Where(delegate(int i)
                {
                    return !refs[i].Append(i).Any(r => alreadyReferenced.Contains(r));
                }).ToArray();
                if (possibleTos.Any())
                {
                    var to = possibleTos[rnd.Next(0, possibleTos.Length - 1)];
                    foreach (var r in parentRefs[from])
                    {
                        refs[r].Add(to);
                        parentRefs[to].Add(r);
                    }

                    debug.Add($"{from}({projectsToMod[from]}) > {to}({projectsToMod[to]})");

                    var sln = solution;
                    //TODO: poss remove
                    // We don't track transative refs - just exclude them here
                    if (sln.GetProjectDependencyGraph()
                        .GetProjectsThatThisProjectTransitivelyDependsOn(projectsToMod[to])
                        .All(i => i != projectsToMod[@from]))
                    {
                        Assert.True(sln.Workspace.TryApplyChanges(
                            sln.AddProjectReference(projectsToMod[from], new ProjectReference(projectsToMod[to]))));
                        solution = ws.CurrentSolution;
                    }
                }
                else
                {
                    full.Add(from);
                }

                incomplete = refs.Where(i => i.Count < Math.Min(depth, projectsToMod.Length))
                    .Select((_, i) => i)
                    .Except(full).ToArray();
            } while (incomplete.Length > 1);

            return ws;
        }

        public AdhocWorkspace GetLinearSolution(int projectCount)
        {
            var ws = new AdhocWorkspace();
            var lastId = addProject(ws, "Project0", null, 0 - projectCount).Id;

            for (int i = 1; i < projectCount; i++)
            {
                lastId = addProject(ws, "Project" + i, lastId, 0 - projectCount + i).Id;
            }

            return ws;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(this.tmp, true);
            }
            catch
            {
                // ignored
            }
        }

        public AdhocWorkspace AddReferencesWithFunc(AdhocWorkspace ws, int start, Func<int, int[]> func)
        {
            int position = start;
            var sln = ws.CurrentSolution;

            int[] actions;
            do
            {
                actions = func(position);
                position = actions.LastOrDefault();
                if (actions.Length > 1)
                {
                    var from = actions.First();
                    for (int i = 1; i < actions.Length - 1; i++)
                    {
                        int to = actions[i];
                        logger.Information($"{from:D3} -> {to:D3}\t{sln.ProjectIds[from]} -> {sln.ProjectIds[to]}");
                        Assert.True(ws.TryApplyChanges(sln.AddProjectReference(sln.ProjectIds[from],
                            new ProjectReference(sln.ProjectIds[to]))));
                        sln = ws.CurrentSolution;
                    }
                }
            } while (actions.Length > 0 && position >= 0);

            return ws;
        }
    }
}
