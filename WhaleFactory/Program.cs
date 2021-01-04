using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommandLine;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace WhaleFactory
{
    class Program
    {
        //TODO: extract common projects in to shared stages and build then cache
        static async Task Main(string[] args)
        {
            await DockerfileGenerator.WriteResources();
            await Parser.Default.ParseArguments<CmdOptions>(args)
                .WithParsedAsync<CmdOptions>(async o =>
                {
                    o.OriginalArgs = string.Join(" ", args);
                    var logger = new LoggerConfiguration()
                        .MinimumLevel.Debug()
                        .WriteTo.Console(o.Verbose ? LogEventLevel.Verbose : LogEventLevel.Warning)
                        .CreateLogger();
                    try
                    {
                        if (!o.Validate(logger.ForContext<CmdOptions>()))
                        {
                            Environment.ExitCode = 101;
                            return;
                        }

                        o.OutputArgs(logger.ForContext<CmdOptions>());

                        DockerfileGenerator generator =
                            new DockerfileGenerator(o.Template, logger.ForContext<DockerfileGenerator>());
                        if (generator.InvalidTemplate)
                        {
                            Environment.ExitCode = 102;
                            return;
                        }

                        using var solutionsData = new SolutionData(o.SlnFile, logger);
                        var testProvider = new TestProvider(logger.ForContext<TestProvider>(), o.ProjectsToTest,
                            o.AutoTestExclude, o.AutoTestDepth);
                        await execute(o, generator, solutionsData, testProvider, logger);
                    }
                    catch (Exception ex)
                    {
                        logger.Fatal(ex, "Exception thrown in Main");
                        throw;
                    }
                });
        }


        private static async Task execute(CmdOptions options, DockerfileGenerator generator, SolutionData solutionsData, TestProvider testProvider, Logger logger)
        {
            object data;
            if (options.InputJson == null)
            {
                await solutionsData.Init();
                if (!options.NoAutoTest)
                {
                    var projects = testProvider.GetTests(solutionsData.GetSolution(), options.PublishedProject);
                    options.ProjectsToTest = options.ProjectsToTest.Concat(projects).Distinct().ToArray();
                }

                await solutionsData.Load(options);

                data = solutionsData.GetDockerData();
            }
            else
            {
                await using var inStr = options.InputJson == string.Empty ? Console.OpenStandardInput() : File.OpenRead(options.InputJson);
                data = await JsonSerializer.DeserializeAsync<object>(inStr);
            }

            await using var outputStr =options.OutputFile?.Open(FileMode.Create, FileAccess.Write) ?? Console.OpenStandardOutput();
            if (options.OutputJson)
            {
                await JsonSerializer.SerializeAsync(outputStr, data, new JsonSerializerOptions{WriteIndented = true});
            }
            else
            {
                await using var outputWriter = new StreamWriter(outputStr);
                if (!options.NoHeader)
                {
                    await generator.WriteHeader(outputWriter, options);
                }

                await generator.Generate(outputWriter, data);
            }

            if (options.OutputFile != null)
            {
                logger.Information("Output saved to " + options.OutputFile);
                outputStr.Close();
            }
        }
    }
}
