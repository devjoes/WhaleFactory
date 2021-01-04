using System.Collections.Immutable;

namespace WhaleFactory
{
    public class DependencyCollection
    {
        public DependencyCollection():this(true){}
        public DependencyCollection(bool initEmpty)
        {
            if (!initEmpty)return;
            this.All = ImmutableArray<DockerProjectData>.Empty;
            this.ExcludePrevious = ImmutableArray<DockerProjectData>.Empty;
        }

        public ImmutableArray<DockerProjectData> All { get; set; }
        public ImmutableArray<DockerProjectData> ExcludePrevious { get; set; }
    }
}