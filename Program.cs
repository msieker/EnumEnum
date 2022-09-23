using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Xml.Linq;
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

public record FoundEnumValue(string Name, string Value, string Comment);
public record FoundEnum(string Name, string Project, string File, int LineNumber, IReadOnlyList<FoundEnumValue> Values);

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


        IEnumerable<FoundEnum> enums = Enumerable.Empty<FoundEnum>();
        if (settings.ProjectPath.EndsWith("sln", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.WriteLine($"Opening solution {settings.ProjectPath}");
            var solution = await workspace.OpenSolutionAsync(settings.ProjectPath);

            enums = (await Task.WhenAll(solution.Projects.Select(ProcessProject))).SelectMany(e => e);
            foreach (var p in solution.Projects)
            {
                await ProcessProject(p);
            }
        }
        else
        {
            AnsiConsole.WriteLine($"Opening project {settings.ProjectPath}");
            var project = await workspace.OpenProjectAsync(settings.ProjectPath);
            enums = await ProcessProject(project);
        }

        foreach (var e in enums.OrderBy(e => e.Name))
        {
            var table = new Table();
            table.Title($"{e.Name} - {e.Project} - {e.File}:{e.LineNumber}");
            table.MarkdownBorder();
            table.AddColumn("Name");
            table.AddColumn("Value");
            table.AddColumn("Comment");
            foreach (var v in e.Values)
            {
                table.AddRow(v.Name, v.Value, v.Comment);
            }
            AnsiConsole.Write(table);
        }
        return 0;
    }

    private async Task<IEnumerable<FoundEnum>> ProcessProject(Project project)
    {
        var basePath = Path.GetDirectoryName(project.FilePath) ?? "";
        var enums = new List<FoundEnum>();

        foreach (var doc in project.Documents.Where(d => d.SupportsSyntaxTree && d.SourceCodeKind == SourceCodeKind.Regular))
        {
            var tree = await doc.GetSyntaxTreeAsync();
            if (tree == null)
            {
                return Enumerable.Empty<FoundEnum>();
            }
            foreach (var syntax in (await tree.GetRootAsync()).DescendantNodesAndSelf().OfType<EnumDeclarationSyntax>())
            {
                var lineSpan = syntax.GetLocation().GetLineSpan();
                var relPath = Path.GetRelativePath(basePath, lineSpan.Path);

                var values = new List<FoundEnumValue>();
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
                    values.Add(new FoundEnumValue(member.Identifier.Text, value, comment));
                }
                
                enums.Add(new FoundEnum(syntax.Identifier.ValueText, project.Name, relPath, lineSpan.StartLinePosition.Line, values.AsReadOnly()));
            }
        }
        return enums;
    }
}