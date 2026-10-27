using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

namespace FishAudio.Testing;

internal static class WorkspaceProbe
{
    public static async Task RunAsync(ProbeOptions options, string repositoryRoot, ProbeReport report)
    {
        var toolchain = report.Toolchain ?? throw new InvalidOperationException();
        toolchain.WorkspaceVersion = Program.VersionOf(typeof(MSBuildWorkspace).Assembly);
        toolchain.CSharpVersion = Program.VersionOf(typeof(CSharpCompilation).Assembly);
        if (toolchain.WorkspaceVersion == "[unavailable]" || toolchain.CSharpVersion == "[unavailable]")
            report.Failures.Add(new("workspace", "toolchain-version-unavailable"));
        report.RequestedProperties.Add("Configuration", "Release");
        report.RequestedProperties.Add("Platform", "AnyCPU");
        report.RequestedProperties.Add("TargetFramework", "net10.0-windows");
        if (options.Coverage) report.RequestedProperties.Add("FishAudioCoverage", "true");

        using var workspace = MSBuildWorkspace.Create(report.RequestedProperties);
        workspace.LoadMetadataForReferencedProjects = false;
        workspace.SkipUnrecognizedProjects = false;
        var observed = new ConcurrentQueue<WorkspaceDiagnostic>();
        using var failureRegistration = workspace.RegisterWorkspaceFailedHandler(args => observed.Enqueue(args.Diagnostic));
        try
        {
            await workspace.OpenProjectAsync(options.Project);
            var solution = workspace.CurrentSolution;
            var projects = solution.Projects.ToArray();
            var testProject = projects.SingleOrDefault(project => ProbePaths.Same(project.FilePath, options.Project));
            var productPath = Path.Combine(repositoryRoot, ProbePaths.ProductProject);
            var productProject = projects.SingleOrDefault(project => ProbePaths.Same(project.FilePath, productPath));
            if (testProject is null || productProject is null
                || !testProject.ProjectReferences.Any(reference => reference.ProjectId == productProject.Id))
                report.Failures.Add(new("source-projects", "required-source-project-reference-missing"));

            var compilations = new Dictionary<ProjectId, Compilation>();
            foreach (var project in projects)
            {
                var evidence = new ProjectEvidence(ProbePaths.Relative(repositoryRoot, project.FilePath));
                report.Projects.Add(evidence);
                try
                {
                    evidence.Documents.AddRange(project.Documents.Select(document => ProbePaths.Relative(repositoryRoot, document.FilePath)));
                    evidence.Analyzers.AddRange(project.AnalyzerReferences.Select(reference => ProbePaths.Relative(repositoryRoot, reference.FullPath)));
                    evidence.ProjectReferences.AddRange(project.ProjectReferences.Select(reference =>
                        ProbePaths.Relative(repositoryRoot, solution.GetProject(reference.ProjectId)?.FilePath)));
                    evidence.MetadataReferenceCount = project.MetadataReferences.Count;
                    if (project.ParseOptions is CSharpParseOptions parseOptions)
                    {
                        evidence.LanguageVersion = parseOptions.LanguageVersion.ToString();
                        evidence.PreprocessorSymbols = parseOptions.PreprocessorSymbolNames.Select(ProbePaths.Token).ToArray();
                    }
                    evidence.Optimization = project.CompilationOptions?.OptimizationLevel.ToString();
                    var compilation = await project.GetCompilationAsync();
                    if (compilation is null)
                    {
                        report.Failures.Add(new("compilation", "compilation-unavailable"));
                        continue;
                    }
                    compilations.Add(project.Id, compilation);
                    evidence.SyntaxTrees.AddRange(compilation.SyntaxTrees.Select(tree => ProbePaths.Relative(repositoryRoot, tree.FilePath)));
                    foreach (var diagnostic in compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                        evidence.Errors.Add(new("Error", ProbePaths.Token(diagnostic.Id), LocationOf(repositoryRoot, diagnostic.Location)));
                    var generated = await project.GetSourceGeneratedDocumentsAsync();
                    evidence.GeneratedDocuments.AddRange(generated.Select(document => ProbePaths.Relative(repositoryRoot, document.FilePath ?? document.Name)));
                    if (evidence.Errors.Count != 0) report.Failures.Add(new("compilation", "compilation-errors"));
                    if (project.CompilationOptions?.OptimizationLevel != OptimizationLevel.Release)
                        report.Failures.Add(new("compilation", "release-optimization-missing"));
                }
                catch (Exception error)
                {
                    report.Failures.Add(new("compilation", "exception", ProbePaths.Token(error.GetType().FullName)));
                }
            }

            if (testProject is not null && productProject is not null
                && compilations.TryGetValue(testProject.Id, out var testCompilation)
                && compilations.TryGetValue(productProject.Id, out var productCompilation))
            {
                ReadEvaluatedIdentity(testCompilation, report);
                CheckProductInvocation(repositoryRoot, testCompilation, productCompilation, report.Checks[0]);
                CheckGeneratedPartial(repositoryRoot, productCompilation, report.Checks[1]);
            }
        }
        finally
        {
            // Diagnostics can be reported without OpenProjectAsync throwing. Retain every recorded occurrence.
            var diagnostics = workspace.Diagnostics.ToList();
            foreach (var diagnostic in observed)
                if (!diagnostics.Contains(diagnostic)) diagnostics.Add(diagnostic);
            report.WorkspaceDiagnostics.AddRange(diagnostics.Select(diagnostic =>
                new DiagnosticEvidence(diagnostic.Kind.ToString(), ProbePaths.DiagnosticCode(diagnostic.Message))));
            if (report.WorkspaceFailureCount != 0) report.Failures.Add(new("workspace", "workspace-failures"));
        }
    }

    private static void ReadEvaluatedIdentity(Compilation compilation, ProbeReport report)
    {
        string? Read(string key) => compilation.Assembly.GetAttributes()
            .Where(attribute => attribute.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyMetadataAttribute"
                && attribute.ConstructorArguments.Length == 2 && attribute.ConstructorArguments[0].Value as string == key)
            .Select(attribute => attribute.ConstructorArguments[1].Value as string).SingleOrDefault();
        report.Toolchain!.EvaluatedTestSdkVersion = ProbePaths.Token(Read("RegressionBuildSdk"));
        report.Toolchain.EvaluatedTestConfiguration = ProbePaths.Token(Read("RegressionBuildConfiguration"));
        if (report.Toolchain.EvaluatedTestSdkVersion == "[unavailable]"
            || report.Toolchain.EvaluatedTestSdkVersion != report.Toolchain.SelectedSdkVersion
            || report.Toolchain.EvaluatedTestConfiguration != "Release")
            report.Failures.Add(new("evaluated-identity", "sdk-or-configuration-missing-or-mismatched"));
    }

    private static void CheckProductInvocation(string root, Compilation tests, Compilation product, SemanticCheck check)
    {
        check.Attempted = true;
        check.Reason = "required-source-or-invocation-missing";
        var source = Path.Combine(root, "tests/STranslate.Plugin.Tts.FishAudio.Tests/NUnitCompatibility/CompatibilityCases.cs");
        var tree = tests.SyntaxTrees.SingleOrDefault(tree => ProbePaths.Same(tree.FilePath, source));
        if (tree is null) return;
        var model = tests.GetSemanticModel(tree);
        var methods = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.ValueText == "SynchronousProductCall"
                && model.GetDeclaredSymbol(method)?.ContainingType.ToDisplayString() == "NUnitCompatibility.CompatibilityCases").ToArray();
        if (methods.Length != 1) return;
        var calls = methods[0].DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(call => call.Expression is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText == "IsValidApiKeyFormat").ToArray();
        if (calls.Length != 1) return;
        var call = calls[0];
        check.Invocation = LocationOf(root, call.GetLocation());
        check.Reason = "product-symbol-unresolved-or-mismatched";
        var info = model.GetSymbolInfo(call);
        if (info.Symbol is not IMethodSymbol symbol || info.CandidateSymbols.Length != 0 || !symbol.IsStatic
            || symbol.Name != "IsValidApiKeyFormat"
            || symbol.ContainingType.ToDisplayString() != "STranslate.Plugin.Tts.FishAudio.Configuration.SettingsValidation"
            || !symbol.ContainingAssembly.Identity.Equals(product.Assembly.Identity)) return;
        var expected = Path.Combine(root, ProbePaths.ProductDirectory, "Configuration/SettingsValidation.cs");
        var declaration = symbol.DeclaringSyntaxReferences.SingleOrDefault(reference => ProbePaths.Same(reference.SyntaxTree.FilePath, expected));
        if (declaration is null || !product.SyntaxTrees.Any(tree => ProbePaths.Same(tree.FilePath, declaration.SyntaxTree.FilePath))) return;
        check.Declaration = LocationOf(root, declaration.GetSyntax().GetLocation());
        check.Passed = true;
        check.Reason = "resolved-product-source-declaration";
    }

    private static void CheckGeneratedPartial(string root, Compilation product, SemanticCheck check)
    {
        check.Attempted = true;
        check.Reason = "required-code-behind-or-invocation-missing";
        var source = Path.Combine(root, ProbePaths.ProductDirectory, "Presentation/View/SettingsView.xaml.cs");
        var tree = product.SyntaxTrees.SingleOrDefault(tree => ProbePaths.Same(tree.FilePath, source));
        if (tree is null) return;
        var model = product.GetSemanticModel(tree);
        var calls = tree.GetRoot().DescendantNodes().OfType<ConstructorDeclarationSyntax>()
            .Where(constructor => constructor.ParameterList.Parameters.Count == 0
                && model.GetDeclaredSymbol(constructor)?.ContainingType.ToDisplayString() == "STranslate.Plugin.Tts.FishAudio.View.SettingsView")
            .SelectMany(constructor => constructor.DescendantNodes().OfType<InvocationExpressionSyntax>())
            .Where(call => call.Expression is IdentifierNameSyntax name && name.Identifier.ValueText == "InitializeComponent").ToArray();
        if (calls.Length != 1) return;
        var call = calls[0];
        check.Invocation = LocationOf(root, call.GetLocation());
        check.Reason = "generated-partial-symbol-unresolved-or-mismatched";
        var info = model.GetSymbolInfo(call);
        if (info.Symbol is not IMethodSymbol symbol || info.CandidateSymbols.Length != 0 || symbol.Parameters.Length != 0
            || symbol.ContainingType.ToDisplayString() != "STranslate.Plugin.Tts.FishAudio.View.SettingsView") return;
        var declarations = symbol.DeclaringSyntaxReferences.Where(reference =>
        {
            var file = ProbePaths.Relative(root, reference.SyntaxTree.FilePath);
            return file.StartsWith(ProbePaths.ProductDirectory + "/obj/", StringComparison.Ordinal)
                && (file.EndsWith("/SettingsView.g.cs", StringComparison.OrdinalIgnoreCase)
                    || file.EndsWith("/SettingsView.g.i.cs", StringComparison.OrdinalIgnoreCase))
                && product.SyntaxTrees.Contains(reference.SyntaxTree);
        }).ToArray();
        if (declarations.Length != 1) return;
        check.Declaration = LocationOf(root, declarations[0].GetSyntax().GetLocation());
        check.Passed = true;
        check.Reason = "resolved-generated-partial-source-declaration";
    }

    private static SourceLocation? LocationOf(string root, Location location)
    {
        if (!location.IsInSource || location.SourceTree is null) return null;
        var position = location.SourceTree.GetLineSpan(location.SourceSpan).StartLinePosition;
        return new(ProbePaths.Relative(root, location.SourceTree.FilePath), position.Line + 1, position.Character + 1);
    }
}
