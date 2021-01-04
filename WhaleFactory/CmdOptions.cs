using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CommandLine;
using Serilog;

namespace WhaleFactory
{
    public class CmdOptions
    {
        [Option('c', "context", Required = true, HelpText = "Docker context", SetName = "vsInput")]
        public DirectoryInfo ContextDir { get; set; }

        [Option('p', "publish", Required = true, HelpText = "Project(s) to be published and included in the final stage. Relative/absolute path or relative to context, always within the docker context.", SetName = "vsInput")]
        public FileInfo PublishedProject { get; set; }

        [Option('s', "solution", Required = true, HelpText = "Solution.sln file. Relative/absolute path or relative to context, always within the docker context.", SetName = "vsInput")]
        public FileInfo SlnFile { get; set; }

        [Option('t', "template", Required = true, HelpText = "Template to use, e.g. 'buildkit'. Path can be absolute or relative to '.', '.\\templates' or '%APPDATALOCAL%\\WhaleFactory\\templates'. The extensions .sbntxt, .scriban-txt, .sbn-txt and .liquid will also be appended.")]
        public string Template { get; set; }


        [Option('T', "test", Required = false, Separator = ';', HelpText = "Explicitly specify projects to test (in addition to project found by auto-test if enabled.) Supports ';' separated paths that are relative/absolute or relative to context, always within the docker context.", SetName = "vsInput")]
        public IEnumerable<FileInfo> ProjectsToTest { get; set; }

        [Option('r', "referenced", Required = false, HelpText = "Do not copy projects that are not required to build the publish/test projects.", SetName = "vsInput")]
        public bool OnlyLoadReferencedProjects { get; set; }

        [Option('o', "order", Required = false, Default = DependencySortOrder.TopologicalSolutionWide, HelpText = "Dictates how dependencies are sorted, this impacts layer caching. Can be TopologicalByProject, TopologicalSolutionWide or DateModified")]
        public DependencySortOrder DependencyOrder { get; set; }

        [Option('O', "output", Required = false, HelpText = "Output to a file instead of terminal.")]
        public FileInfo OutputFile { get; set; }

        [Option('v', "verbose", Required = false, HelpText = "Set output to verbose messages.")]
        public bool Verbose { get; set; }

        [Option('a', "no-auto-test", Required = false, HelpText = "Don't automatically include all test projects that cover any of the built code.", SetName = "vsInput")]
        public bool NoAutoTest { get; set; }

        [Option('e', "auto-exclude", Required = false, HelpText = "Ignore automatically found test projects that match this pattern. (E.g. '(?i)(ui|integration)tests?'", SetName = "vsInput")]
        public string AutoTestExclude { get; set; }

        [Option('d', "auto-depth", Required = false, HelpText = "Only search for test projects that cover code n references away from the published project.", SetName = "vsInput")]
        public int AutoTestDepth { get; set; }

        [Option('h', "no-header", Required = false, HelpText = "Don't add header information to the top of the Dockerfile.")]
        public bool NoHeader { get; set; }

        [Option('j', "json", Required = false, HelpText = "Output the JSON of the object used to populate the template opposed to the populated template.")]
        public bool OutputJson { get; set; }

        [Option('i', "input", Required = false, HelpText = "Populate the template with JSON data from the supplied file opposed to the solution. If set to \"\" then read from STDIN.", SetName = "jsonInput")]
        public string InputJson { get; set; }

        //TODO: output CI trigger paths?
        //TODO: make accept percentage or project count
        [Option('C', "common-threshold", Required = false, Default = 0.5f,HelpText = "A dependency will be considered common and only copied once if it used by at least this fraction of the projects.", SetName = "vsInput")]
        public float CommonDepsThreshold { get; set; }

        public static string AppDataPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), Assembly.GetExecutingAssembly().GetName().Name!);
        public static string TemplatePath => Path.Combine(CmdOptions.AppDataPath, "templates");
        internal string OriginalArgs { get; set; }
        


        public bool Validate(ILogger logger)
        {
            FileInfo ValidatePath(DirectoryInfo directoryInfo, FileInfo file, IEnumerable<string> suffixes = null)
            {
                var fileInfo = file;
                if (fileInfo.Exists)
                {
                    return fileInfo;
                }

                List<string> tried = new List<string>(new[] { fileInfo.FullName });
                foreach (var suffix in (suffixes??new string[0]).Prepend(string.Empty))
                {
                    fileInfo = new FileInfo(System.IO.Path.Combine(directoryInfo.FullName,
                        Path.GetRelativePath(Environment.CurrentDirectory, file.FullName), suffix));
                    if (fileInfo.Exists)
                    {
                        return fileInfo;
                    }
                    tried.Add(fileInfo.FullName);
                }

                logger.Error($"Could not find {fileInfo.Name} (tried {string.Join(", ", tried)})");
                return null;
            }

            bool valid = true;
            if (!this.ContextDir.Exists)
            {
                logger.Error($"{this.ContextDir} does not exist");
                valid = false;
            }
            else
            {
                var exts = new[] {".csproj", ".vbproj", ".fsproj"};
                this.SlnFile = ValidatePath(this.ContextDir, this.SlnFile);
                valid &= this.SlnFile != null;
                this.PublishedProject = ValidatePath(this.ContextDir, this.PublishedProject, exts.Select(e => this.PublishedProject.Name +e));
                valid &= this.PublishedProject != null;
                var projects = this.ProjectsToTest.ToArray();
                for (int i = 0; i < projects.Length; i++)
                {
                    var proj = projects[i];
                    var fi = ValidatePath(this.ContextDir, proj, exts.Select(e => proj.Name + e));
                    valid &= fi != null;
                    projects[i] = fi;
                }

                this.ProjectsToTest = projects;
            }

            return valid;
        }

        public void OutputArgs(ILogger logger)
        {
            foreach (var p in this.GetType().GetProperties(BindingFlags.Public|BindingFlags.Instance))
            {
                var value = p.GetValue(this);
                var valueStr = value?.ToString();
                if (value is IEnumerable<object> arr)
                {
                    valueStr = "[";
                    foreach (var i in arr)
                    {
                        valueStr += valueStr.Length == 1 ? i : (", " + i);
                    }
                    valueStr += "]";
                }
                logger.Debug($"{p.Name}: {valueStr}");
            }
        }
    }
}