using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace WhaleFactory
{
    //public class DockerDataConverter : JsonConverter<SerializableDockerData>
    //{
    //    public override SerializableDockerData Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    //    {
    //        throw new NotImplementedException();
    //    }

    //    public override void Write(Utf8JsonWriter writer, SerializableDockerData value, JsonSerializerOptions options)
    //    {
    //        throw new NotImplementedException();
    //    }
    //}
    //public class DockerDataConverterFactory : JsonConverterFactory
    //{
    //    public override bool CanConvert(Type typeToConvert)=>typeToConvert==typeof(SerializableDockerData);

    //    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    //    {
    //        return new DockerDataConverter();
    //    }
    //}

    public class SerializableDockerData
    {
        public SerializableDockerData() : this(true) { }
        public SerializableDockerData(bool deserialized)
        {
            if (!deserialized)
            {
                return;
            }

            // We normally lazy load these and unset Default as we go
            this.ProjFiles = ImmutableArray<string>.Empty;
            this.Dirs = ImmutableArray<string>.Empty;
            this.Tests = ImmutableArray<DockerProjectData>.Empty;
            this.CommonDependencies = ImmutableArray<DockerProjectData>.Empty;
        }
        public virtual bool OnlyRestoreReferencedProjects { get; set; }
        public virtual string Template { get; set; }

        public virtual ImmutableArray<string> ProjFiles { get; set; }

        public virtual ImmutableArray<string> Dirs { get; set; }

        public virtual ImmutableArray<DockerProjectData> Tests { get; set; }
        public virtual ImmutableArray<DockerProjectData> CommonDependencies { get; set; }
        public virtual DockerProjectData MainProject { get; set; }

        public virtual FrameworkData Frameworks { get; set; }
        public virtual string SolutionName { get; set; }
        public virtual string SolutionPath { get; set; }

    }
    public class DockerData : SerializableDockerData
    {
        private readonly Lazy<ImmutableArray<string>> projFiles;
        private readonly Lazy<ImmutableArray<string>> dirs;
        private readonly Lazy<ImmutableArray<DockerProjectData>> tests;
        private readonly Lazy<DockerProjectData> mainProject;
        private readonly Lazy<ImmutableArray<DockerProjectData>> commonDeps;

        public DockerData(Func<ImmutableArray<string>> getProjFiles, Func<ImmutableArray<string>> getDirs, Func<ImmutableArray<DockerProjectData>> getTests, Func<DockerProjectData> getMainProject) : base(false)
        {
            this.projFiles = new Lazy<ImmutableArray<string>>(getProjFiles);
            this.dirs = new Lazy<ImmutableArray<string>>(getDirs);
            this.tests = new Lazy<ImmutableArray<DockerProjectData>>(getTests);
            this.commonDeps = new Lazy<ImmutableArray<DockerProjectData>>(this.getCommonDeps);
            this.mainProject = new Lazy<DockerProjectData>(getMainProject);
        }

        private ImmutableArray<DockerProjectData> getCommonDeps()
        {
            var allProjectsBuilt = this.tests.Value.Prepend(this.mainProject.Value).ToArray();
            Dictionary<Guid,int> commonDepCounts = new Dictionary<Guid, int>();
            foreach (var p in allProjectsBuilt) // Dont use the public prop or we will trigger filterCommonDeps
            {
                foreach (var dep in p.Dependencies.All)
                {
                    if (!commonDepCounts.ContainsKey(dep.Id))
                    {
                        commonDepCounts.Add(dep.Id, 0);
                    }
                    commonDepCounts[dep.Id]++;
                }
            }

            var commonIds = commonDepCounts.Keys
                .Where(id => commonDepCounts[id] / (float) allProjectsBuilt.Length >= this.CommonDepsThreshold).ToHashSet();

            var common = allProjectsBuilt
                .SelectMany(p => p.Dependencies.All)
                .GroupBy(g => g.Id)
                .Select(g => g.First())
                .Where(d => commonIds.Contains(d.Id));
            return common.OrderBy(d => DependencyOrdering.GetOrderedProjectIndex(d.Id)).ToImmutableArray();
        }

        public override ImmutableArray<string> ProjFiles => this.projFiles.Value;

        public override ImmutableArray<string> Dirs => this.dirs.Value;

        public override ImmutableArray<DockerProjectData> Tests => this.filterCommonDeps(this.tests.Value);
        public override DockerProjectData MainProject => this.filterCommonDeps(new[] { this.mainProject.Value }).SingleOrDefault();
        public override ImmutableArray<DockerProjectData> CommonDependencies => this.commonDeps.Value;
        public float CommonDepsThreshold { get; set; }
        public DependencyOrder DependencyOrdering { get; set; }

        private ImmutableArray<DockerProjectData> filterCommonDeps(IEnumerable<DockerProjectData> depsInput, int depth = 0)
        {
            if (depsInput == null
                || depth > 100) // Circular ref? This shouldn't happen with a valid solution
            {
                return new ImmutableArray<DockerProjectData>();
            }
            var deps = depsInput.ToArray();
            var common = this.commonDeps.Value
                .Select(d => d.Id)
                .Concat(deps.Select(d => d.Id))
                .ToHashSet();

            foreach (var dockerProjectData in deps)
            {
                // We cant do this anymore since common will be different depending on where we came from
                //if (!dockerProjectData.DependencyLoader.Value.ExcludePrevious.IsDefault) // Only visit once
                //{
                //    continue;
                //}

                var orig = dockerProjectData.DependencyLoader;
                dockerProjectData.DependencyLoader = new Lazy<DependencyCollection>(() =>
                {
                    var val = orig.Value;
                    this.filterCommonDeps(val.All, depth + 1);
                    val.ExcludePrevious = val.All
                        .Where(d => !common.Contains(d.Id)).ToImmutableArray();
                    return val;
                });
            }

            return deps.ToImmutableArray();
        }

    }
}