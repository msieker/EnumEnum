using System.ComponentModel;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Spectre.Console;
using Spectre.Console.Cli;

var app = new CommandApp<EnumEnumsCommand>();
app.Configure(c =>
{
    c.PropagateExceptions();
});
return await app.RunAsync(args);

internal sealed class EnumEnumsCommand : AsyncCommand<EnumEnumsCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [Description("Path to project or solution file.")]
        [CommandArgument(0, "[projectPath]")]
        public string? ProjectPath { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        MSBuildLocator.RegisterDefaults();

        using var workspace = MSBuildWorkspace.Create();

        if (string.IsNullOrEmpty(settings.ProjectPath))
        {
            AnsiConsole.MarkupLine("[red]ProjectPath is a required parameter[/]");
            return -1;
        }

        if (settings.ProjectPath.EndsWith("sln", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.WriteLine($"Opening solution {settings.ProjectPath}");
            var solution = await workspace.OpenSolutionAsync(settings.ProjectPath);

            foreach (var p in solution.Projects)
            {
                await ProcessProject(p);
            }
        }
        else
        {
            AnsiConsole.WriteLine($"Opening project {settings.ProjectPath}");
            var project = await workspace.OpenProjectAsync(settings.ProjectPath);
            await ProcessProject(project);
        }
        return 0;
    }

    private async Task ProcessProject(Project project)
    {
        var basePath = Path.GetDirectoryName(project.FilePath);

        foreach (var doc in project.Documents.Where(d => d.SupportsSyntaxTree && d.SourceCodeKind == SourceCodeKind.Regular))
        {
            var tree = await doc.GetSyntaxTreeAsync();

            foreach (var syntax in (await tree.GetRootAsync()).DescendantNodesAndSelf().OfType<EnumDeclarationSyntax>())
            {
                var lineSpan = syntax.GetLocation().GetLineSpan();
                var relPath = Path.GetRelativePath(basePath, lineSpan.Path);

                var table = new Table();
                table.Title($"{syntax.Identifier.ValueText} - {project.Name} - {relPath}:{lineSpan.StartLinePosition.Line}");

                table.AddColumn("Name");
                table.AddColumn("Value");
                table.AddColumn("Comment");

                foreach (var member in syntax.Members)
                {
                    var value = "";
                    var comment = "";

                    if (member.HasLeadingTrivia)
                    {
                        var trivia = member.GetLeadingTrivia().Select(t => t.GetStructure()).OfType<DocumentationCommentTriviaSyntax>().FirstOrDefault();
                        var summary = trivia?.ChildNodes().OfType<XmlElementSyntax>().FirstOrDefault(x => x.StartTag.Name.ToString().Equals("summary"));


                        comment = summary?.Content.ToString().Replace("///", "").Trim() ?? "";
                    }

                    if (member.EqualsValue != null)
                    {
                        value = member.EqualsValue.Value.ToString();
                    }
                    table.AddRow(member.Identifier.ValueText, value, comment);
                }

                AnsiConsole.Write(table);

            }
        }

        //var compilation = await project.GetCompilationAsync();

        //if (compilation == null)
        //{
        //    AnsiConsole.MarkupLineInterpolated($"[red]Could not get compilation for project {project.Name}[/]");
        //}

        //compilation.
    }
}