using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Scriban;
using Serilog;

namespace WhaleFactory
{
    internal class DockerfileGenerator
    {
        private readonly ILogger logger;
        private readonly Template template;
        private string templatePath;

        public DockerfileGenerator(string templateFile, ILogger logger)
        {
            this.logger = logger;
            this.templatePath = this.getTemplateFile(templateFile);
            if (this.templatePath == null)
            {
                this.InvalidTemplate = true;
                return;
            }
            this.template = Template.Parse(File.ReadAllText(this.templatePath), this.templatePath);
            if (this.template.HasErrors)
            {
                logger.Fatal($"Error parsing template: {this.templatePath}");
                foreach (var message in this.template.Messages)
                {
                    logger.Error($"{message.Type}\t{message.Span.ToStringSimple()}:\t{message.Message}");
                }

                this.InvalidTemplate = true;
            }
        }

        public bool InvalidTemplate { get; }

        private string getTemplateFile(string templateFile)
        {
            var folders = new[] { CmdOptions.TemplatePath, Environment.CurrentDirectory, "templates" };
            var exts = new[] { ".sbntxt", ".scriban-txt", ".scriban-txt", ".scriban-txt" };
            var paths = folders.SelectMany(f => exts.Select(e => Path.Combine(f, templateFile + e))).ToArray();
            var file = paths.FirstOrDefault(File.Exists) ?? new FileInfo(templateFile).FullName;
            if (File.Exists(file))
            {
                return file;
            }
            foreach (var path in paths)
            {
                this.logger.Error("Could not find template: " + path);
            }
            this.logger.Fatal("Could not load template " + file);
            return null;
        }

        public async Task Generate(StreamWriter outputStr, object data)
        {
            var output = await this.template.RenderAsync(data);
            await outputStr.WriteAsync(output);
        }

        public async Task WriteHeader(StreamWriter outputStr, CmdOptions options)
        {
            string gitInfo = await this.getGitInfo(options.SlnFile);
            using SHA1Managed sha1 = new SHA1Managed();
            var hashBytes = sha1.ComputeHash(await File.ReadAllBytesAsync(this.templatePath));
            var hash = string.Concat(hashBytes.Select(b => b.ToString("x2")));
            var asm = Assembly.GetExecutingAssembly();
            string appInfo = $"({asm.GetName().Name} {asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion})";
            await outputStr.WriteLineAsync($"# Template created from {options.SlnFile.Name}{gitInfo} with {Path.GetFileName(this.templatePath)} (SHA:{hash}) on {DateTime.Now.ToShortDateString()} {appInfo}");
            await outputStr.WriteLineAsync($"# {Assembly.GetExecutingAssembly().GetName().CodeBase?.Split('/').Last()} {options.OriginalArgs}");
        }

        private async Task<string> getGitInfo(FileInfo slnFile)
        {
            var git = Utils.GetGitDir(slnFile.Directory);
            if (git != null)
            {
                var fiGitRef = new FileInfo(Path.Combine(git.FullName, "HEAD"));
                if (fiGitRef.Exists)
                {
                    string gitRef = (await File.ReadAllTextAsync(fiGitRef.FullName)).Trim().Split('/').Last();
                    var fiGitSha = new FileInfo(Path.Combine(git.FullName, "refs","heads", gitRef));

                    string sha = fiGitSha.Exists
                        ? " #" + (await File.ReadAllTextAsync(fiGitSha.FullName)).Remove(8)
                        : string.Empty;
                    return $" ({gitRef}{sha})";
                }
            }

            return string.Empty;
        }

        [SuppressMessage("ReSharper", "ConditionIsAlwaysTrueOrFalse")]
        public static async Task WriteResources()
        {
            var asm = Assembly.GetExecutingAssembly();
            const string trimTo = ".templates.";
            var templates = asm.GetManifestResourceNames()
                .Where(r => r.EndsWith(".sbntxt") && r.Contains(trimTo))
                .ToDictionary(k => k, v => v.Substring(v.IndexOf(trimTo, StringComparison.Ordinal) + trimTo.Length));

            if (!Directory.Exists(CmdOptions.TemplatePath))
            {
                Directory.CreateDirectory(CmdOptions.TemplatePath);
            }

            foreach (var resource in templates.Keys)
            {
                var path = Path.Combine(CmdOptions.TemplatePath, templates[resource]);
                var create = !File.Exists(path)
#if DEBUG
                             || true
#endif
                    ;
                if (create)
                {
                    await using var str = asm.GetManifestResourceStream(resource);
                    using var reader = new StreamReader(str ?? throw new InvalidOperationException());
                    var content = await reader.ReadToEndAsync();
                    await File.WriteAllTextAsync(path, content);
                }
            }
        }
    }
}