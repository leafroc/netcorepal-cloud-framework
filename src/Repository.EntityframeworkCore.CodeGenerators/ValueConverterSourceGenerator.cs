﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Emit;

namespace NetCorePal.Extensions.Repository.EntityframeworkCore.CodeGenerators
{
    [Generator]
    public class ValueConverterSourceGenerator : ISourceGenerator
    {
        public void Execute(GeneratorExecutionContext context)
        {
            context.AnalyzerConfigOptions.GlobalOptions.TryGetValue("build_property.RootNamespace", out var rootNamespace);
            if (rootNamespace == null)
            {
                return;
            }

            var compilation = context.Compilation;
            var r1 = compilation.DirectiveReferences.ToArray();
            var r2 = compilation.ExternalReferences;
            var r3 = compilation.References;
            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                if (syntaxTree.TryGetText(out var sourceText) &&
                    !sourceText.ToString().Contains("StronglyTypedId"))
                {
                    continue;
                }
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                if (semanticModel == null)
                {
                    continue;
                }

                var typeDeclarationSyntaxs = syntaxTree.GetRoot().DescendantNodesAndSelf().OfType<TypeDeclarationSyntax>();
                foreach (var tds in typeDeclarationSyntaxs)
                {
                    Generate(context, semanticModel, tds, rootNamespace);
                }

            }


            var refs = compilation.References.Where(p => p.Properties.Kind == MetadataImageKind.Assembly).ToList();


            foreach (var r in refs)
            {
                var assembly = compilation.GetAssemblyOrModuleSymbol(r) as IAssemblySymbol;

                if (assembly == null)
                {
                    continue;
                }




                var nameprefix = compilation.AssemblyName?.Split('.').First();


                if (assembly.Name.StartsWith(nameprefix))
                {


                    var types = GetAllTypes(assembly);

                    foreach (var type in types)
                    {

                        if (type.TypeKind == TypeKind.Class && type.AllInterfaces.Any(p => p.Name == "IStronglyTypedId"))
                        {

                            Generate(context, type, rootNamespace);
                        }



                    }


                }

            }





        }

        static List<INamedTypeSymbol> GetAllTypes(IAssemblySymbol assemblySymbol)
        {
            var types = new List<INamedTypeSymbol>();
            GetTypesInNamespace(assemblySymbol.GlobalNamespace, types);
            return types;
        }

        static void GetTypesInNamespace(INamespaceSymbol namespaceSymbol, List<INamedTypeSymbol> types)
        {
            // 获取当前命名空间中的类型
            types.AddRange(namespaceSymbol.GetTypeMembers());

            // 遍历所有子命名空间
            foreach (var subNamespaceSymbol in namespaceSymbol.GetNamespaceMembers())
            {
                GetTypesInNamespace(subNamespaceSymbol, types);
            }
        }


        private void Generate(GeneratorExecutionContext context, INamedTypeSymbol idType, string rootNamespace)
        {
            var ns = idType.ContainingNamespace.ToString();
            string className = idType.Name;
            var stronglyTypedId = idType.AllInterfaces
                .SingleOrDefault(t => t.Name == "IStronglyTypedId");
            if (stronglyTypedId == null) return;
            string source = $@"// <auto-generated/>
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using {ns};
namespace {rootNamespace}.ValueConverters
{{
    public partial class {className}ValueConverter : ValueConverter<{className}, {stronglyTypedId.TypeArguments.First().Name}>
    {{
        public  {className}ValueConverter() : base(p => p.Id, p => new {className}(p)) {{ }}
    }}
}}
";
            context.AddSource($"{className}ValueConverter.g.cs", source);
        }



        private void Generate(GeneratorExecutionContext context, SemanticModel semanticModel, TypeDeclarationSyntax classDef, string rootNamespace)
        {
            var symbol = semanticModel.GetDeclaredSymbol(classDef);
            if (!(symbol is INamedTypeSymbol)) return;
            INamedTypeSymbol namedTypeSymbol = (INamedTypeSymbol)symbol;


            var stronglyTypedId = namedTypeSymbol.AllInterfaces
                .SingleOrDefault(t => t.Name == "IStronglyTypedId");
            if (stronglyTypedId == null) return;
            string ns = namedTypeSymbol.ContainingNamespace.ToString();
            string codeNamespace = $"{context.Compilation.GlobalNamespace.Name}.ValueConverters";
            string className = namedTypeSymbol.Name;
            string source = $@"// <auto-generated/>
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using {ns};
namespace {rootNamespace}.ValueConverters
{{
    public partial class {className}ValueConverter : ValueConverter<{className}, {stronglyTypedId.TypeArguments.First().Name}>
    {{
        public  {className}ValueConverter() : base(p => p.Id, p => new {className}(p)) {{ }}
    }}
}}
";
            context.AddSource($"{className}ValueConverter.g.cs", source);
        }

        public void Initialize(GeneratorInitializationContext context)
        {

        }
    }
}