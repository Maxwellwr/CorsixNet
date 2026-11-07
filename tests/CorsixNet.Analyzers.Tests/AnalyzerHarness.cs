using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CorsixNet.Analyzers.Tests;

/// <summary>
/// Compiles a source snippet against the running framework and returns the
/// determinism diagnostics as (id, flagged source text) pairs.
/// </summary>
internal static class AnalyzerHarness
{
    private static readonly ImmutableArray<MetadataReference> References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();

    public static async Task<IReadOnlyList<(string Id, string Text)>> AnalyzeAsync(string source, bool enabled = true)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            "AnalyzerTest",
            [tree],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var compileErrors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString());
        Assert.Empty(compileErrors);

        var options = new AnalyzerOptions([], new OptionsProvider(enabled));
        var diagnostics = await compilation
            .WithAnalyzers([new DeterminismAnalyzer()], options)
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);

        return diagnostics
            .OrderBy(d => d.Location.SourceSpan.Start)
            .Select(d => (d.Id, source.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length)))
            .ToList();
    }

    private sealed class OptionsProvider(bool enabled) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(enabled);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Options.Empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Options.Empty;
    }

    private sealed class Options(bool enabled) : AnalyzerConfigOptions
    {
        public static readonly Options Empty = new(enabled: false);

        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
        {
            if (enabled && key == DeterminismAnalyzer.EnableOption)
            {
                value = "true";
                return true;
            }

            value = null;
            return false;
        }
    }
}
