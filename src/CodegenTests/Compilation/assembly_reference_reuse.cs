using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using JasperFx.RuntimeCompiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;
using Xunit;

namespace CodegenTests.Compilation;

public class assembly_reference_reuse : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "jasperfx-reference-reuse-" + Guid.NewGuid());

    public assembly_reference_reuse()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, true);
    }

    [Fact]
    public void separate_generators_share_one_reference_per_assembly_file()
    {
        var first = new AssemblyGenerator();
        first.ReferenceAssemblyContainingType<assembly_reference_reuse>();
        first.Generate("public class ReuseOne {}");

        var second = new AssemblyGenerator();
        second.ReferenceAssemblyContainingType<assembly_reference_reuse>();
        second.Generate("public class ReuseTwo {}");

        var assembly = typeof(assembly_reference_reuse).Assembly;
        referenceTo(second, assembly).ShouldBeSameAs(referenceTo(first, assembly));
    }

    [Fact]
    public void assemblies_in_a_collectible_context_are_not_cached()
    {
        var path = Path.Combine(_directory, "CollectibleLibrary.dll");
        File.WriteAllBytes(path, compileLibrary("CollectibleLibrary", "public class Collectible {}"));

        var context = new AssemblyLoadContext("collectible", isCollectible: true);
        var assembly = context.LoadFromAssemblyPath(path);

        var first = new AssemblyGenerator();
        first.ReferenceAssembly(assembly);
        var second = new AssemblyGenerator();
        second.ReferenceAssembly(assembly);

        referenceTo(second, assembly).ShouldNotBeSameAs(referenceTo(first, assembly));

        context.Unload();
    }

    private static PortableExecutableReference referenceTo(AssemblyGenerator generator, Assembly assembly)
    {
        return generator.References.OfType<PortableExecutableReference>().Single(x => x.FilePath == assembly.Location);
    }

    private static byte[] compileLibrary(string name, string code)
    {
        var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(code)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        compilation.Emit(stream).Success.ShouldBeTrue();
        return stream.ToArray();
    }
}
