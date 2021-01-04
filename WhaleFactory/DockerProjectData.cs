using System;
using System.Collections.Immutable;
using System.IO;
using Microsoft.CodeAnalysis;

namespace WhaleFactory
{
    public class DockerProjectData
    {
        private readonly Project project;
        private readonly DirectoryInfo context;

        public DockerProjectData()
        {
            this.deserialized = true;
            this.dependencies = new DependencyCollection(true);
        }
        public DockerProjectData(Project project, DirectoryInfo context, Func<ImmutableArray<DockerProjectData>> getDeps)
        {
            this.project = project;
            this.context = context;
            this.DependencyLoader = new Lazy<DependencyCollection>(()=>new DependencyCollection(false){All = getDeps()});
        }

        public override int GetHashCode()
        {
            if (this.Id == default)
            {
                // This data can be supplied as json so who knows
                throw new InvalidOperationException("Project "+this.Name+" does not have an Id");
            }

            return this.Id.GetHashCode();
        }

        public override bool Equals(object? obj)
        {
            if (this.Id == default)
            {
                // This data can be supplied as json so who knows
                throw new InvalidOperationException("Project " + this.Name + " does not have an Id");
            }
            return (obj as DockerProjectData)?.GetHashCode() == this.GetHashCode();
        }

        private bool dependenciesLoaded;
        public bool DependenciesLoaded
        {
            get => this.deserialized ? this.dependenciesLoaded :  this.DependencyLoader.IsValueCreated;
            set => this.dependenciesLoaded=value;
        }

        public Project GetProject() => this.project;

        private string name;
        public string Name
        {
            get => this.deserialized ? this.name :  this.project.Name;
            set => this.name=value;
        }

        private string fileName;
        public string FileName
        {
            get => this.deserialized ? this.fileName  :  System.IO.Path.GetFileName(this.project.FilePath);
            set => this.fileName = value;
        }

        private string path;
        public string Path
        {
            get => this.deserialized ? this.path  :  Utils.ConvertPath(this.project.FilePath, true, this.context);
            set => this.path = value;
        }


        private string directory;
        public string Directory
        {
            get => this.deserialized ? this.directory  :  Utils.ConvertPath(System.IO.Path.GetDirectoryName(this.project.FilePath), true, this.context);
            set => this.directory = value;
        }

        private string assemblyName;
        public string AssemblyName
        {
            get => this.deserialized ? this.assemblyName  :  this.project.AssemblyName;
            set => this.assemblyName = value;
        }

        private DependencyCollection dependencies;
        public DependencyCollection Dependencies
        {
            get => this.deserialized ? this.dependencies  :  this.DependencyLoader.Value;
            set => this.dependencies = value;
        }

        private Guid id;
        private readonly bool deserialized;

        public Guid Id
        {
            get => this.deserialized ? this.id  :  this.project.Id.Id;
            set => this.id = value;
        }
        
        internal Lazy<DependencyCollection> DependencyLoader { get; set; }
        
    }
}