﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NetCorePal.Extensions.Repository.EntityFrameworkCore.SourceGenerators
{
    [Generator]
    public class AppDbContextBaseSourceGenerator : ISourceGenerator
    {
        private readonly IReadOnlyCollection<string> dbContextBaseNames = new[]
            { "AppDbContextBase", "AppIdentityDbContextBase", "AppIdentityUserContextBase" };

        public void Execute(GeneratorExecutionContext context)
        {
            try
            {
                context.AnalyzerConfigOptions.GlobalOptions.TryGetValue("build_property.RootNamespace",
                    out var rootNamespace);
                if (rootNamespace == null)
                {
                    return;
                }

                var compilation = context.Compilation;
                foreach (var syntaxTree in compilation.SyntaxTrees.ToList())
                {
                    if (context.CancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    if (syntaxTree.TryGetText(out var sourceText) &&
                        !dbContextBaseNames.Any(p => sourceText.ToString().Contains(p)))
                    {
                        continue;
                    }

                    var semanticModel = compilation.GetSemanticModel(syntaxTree);
                    if (semanticModel == null)
                    {
                        continue;
                    }

                    var typeDeclarationSyntaxs =
                        syntaxTree.GetRoot().DescendantNodesAndSelf().OfType<TypeDeclarationSyntax>();
                    foreach (var tds in typeDeclarationSyntaxs)
                    {
                        var symbol = semanticModel.GetDeclaredSymbol(tds);
                        if (symbol is INamedTypeSymbol namedTypeSymbol)
                        {
                            if (namedTypeSymbol.IsAbstract)
                            {
                                return;
                            }

                            if (!dbContextBaseNames.Contains(namedTypeSymbol.BaseType?.Name))
                            {
                                return;
                            }

                            List<INamedTypeSymbol> ids = GetAllStrongTypedId(context);
                            GenerateValueConverters(context, namedTypeSymbol, ids, rootNamespace);
                            Generate(context, namedTypeSymbol, ids, rootNamespace);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                var sb = new StringBuilder();
                sb.AppendLine(ex.Message);
                sb.AppendLine(ex.StackTrace);
                if (ex.InnerException != null)
                {
                    sb.AppendLine("======InnerException========");
                    sb.AppendLine(ex.InnerException.Message);
                    sb.AppendLine(ex.InnerException.StackTrace);
                }

                context.AddSource($"AppDbContextBaseSourceGeneratorError.g.cs", sb.ToString());
            }
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


        private void Generate(GeneratorExecutionContext context, INamedTypeSymbol dbContextType,
            List<INamedTypeSymbol> ids, string rootNamespace)
        {
            var ns = dbContextType.ContainingNamespace.ToString();
            string className = dbContextType.Name;


            StringBuilder sb = new();

            foreach (var id in ids)
            {
                var idName = id.Name;
                sb.AppendLine($@"            configurationBuilder.Properties<global::{id.ContainingNamespace}.{id.Name}>().HaveConversion<global::{id.ContainingNamespace}.ValueConverters.{id.Name}ValueConverter>();");
            }

            string source = $@"// <auto-generated/>
using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;
namespace {ns}
{{
    public partial class {className}
    {{
        protected override void ConfigureStronglyTypedIdValueConverter(ModelConfigurationBuilder configurationBuilder)
        {{
{sb}
        }}
    }}
}}
";
            context.AddSource($"{className}ValueConverterConfigure.g.cs", source);
        }

        void GenerateValueConverters(GeneratorExecutionContext context, INamedTypeSymbol dbContextType,
            List<INamedTypeSymbol> ids, string rootNamespace)
        {
            foreach (var idType in ids)
            {
                var ns = idType.ContainingNamespace.ToString();
                StringBuilder source = new($@"// <auto-generated/>
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System;
namespace {ns}.ValueConverters
{{
");

                var className = $"{idType.ContainingNamespace}.{idType.Name}";
                source.Append($@"   
    public class {idType.Name}ValueConverter : ValueConverter<{className}, {idType.AllInterfaces.First(t => t.Name == "IStronglyTypedId").TypeArguments[0].Name}>
    {{
        public  {idType.Name}ValueConverter() : base(p => p.Id, p => new {className}(p)) {{ }}
    }}
");


                source.Append($@"
}}
");
                context.AddSource($"{ns}.{idType.Name}ValueConverters.g.cs", source.ToString());
            }
        }


        List<INamedTypeSymbol> GetAllTypes(IAssemblySymbol assemblySymbol)
        {
            var types = new List<INamedTypeSymbol>();
            GetTypesInNamespace(assemblySymbol.GlobalNamespace, types);
            return types;
        }

        List<INamedTypeSymbol> GetAllStrongTypedId(GeneratorExecutionContext context)
        {
            var list = GetStrongTypedIdFromCurrentProject(context);
            list.AddRange(GetStrongTypedIdFromReferences(context));
            return list;
        }


        List<INamedTypeSymbol> GetStrongTypedIdFromCurrentProject(GeneratorExecutionContext context)
        {
            List<INamedTypeSymbol> strongTypedIds = new();
            var compilation = context.Compilation;
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

                var typeDeclarationSyntaxs =
                    syntaxTree.GetRoot().DescendantNodesAndSelf().OfType<TypeDeclarationSyntax>();
                foreach (var tds in typeDeclarationSyntaxs)
                {
                    var symbol = semanticModel.GetDeclaredSymbol(tds);
                    if (symbol == null) continue;
                    INamedTypeSymbol namedTypeSymbol = (INamedTypeSymbol)symbol;
                    if (namedTypeSymbol == null) continue;

                    if (IsStrongTypedId(namedTypeSymbol))
                    {
                        strongTypedIds.Add(namedTypeSymbol);
                    }
                }
            }

            return strongTypedIds;
        }

        List<INamedTypeSymbol> GetStrongTypedIdFromReferences(GeneratorExecutionContext context)
        {
            var compilation = context.Compilation;
            var refs = compilation.References.Where(p => p.Properties.Kind == MetadataImageKind.Assembly).ToList();

            List<INamedTypeSymbol> strongTypedIds = new();
            foreach (var r in refs)
            {
                if (compilation.GetAssemblyOrModuleSymbol(r) is not IAssemblySymbol assembly)
                {
                    continue;
                }

                var nameprefix = compilation.AssemblyName?.Split('.')[0];
                if (assembly.Name.StartsWith(nameprefix))
                {
                    var types = GetAllTypes(assembly);
                    strongTypedIds.AddRange(from type in types
                                            where IsStrongTypedId(type)
                                            select type);
                }
            }

            return strongTypedIds;
        }

        bool IsStrongTypedId(INamedTypeSymbol type)
        {
            return type.TypeKind == TypeKind.Class &&
                   type.AllInterfaces.Any(p => p.Name == "IStronglyTypedId");
        }


        void TryGetIdNamedTypeSymbol(GeneratorExecutionContext context, INamedTypeSymbol namedTypeSymbol,
            List<INamedTypeSymbol> ids,
            List<INamedTypeSymbol> allType)
        {
            if (allType.Exists(t =>
                    SymbolEqualityComparer.Default.Equals(t.ContainingNamespace, namedTypeSymbol.ContainingNamespace) &&
                    t.Name == namedTypeSymbol.Name))
            {
                return;
            }

            allType.Add(namedTypeSymbol);
            if (!namedTypeSymbol.IsAbstract && !namedTypeSymbol.IsGenericType &&
                namedTypeSymbol.AllInterfaces.Any(p => p.Name == "IStronglyTypedId") &&
                !ids.Exists(t =>
                    SymbolEqualityComparer.Default.Equals(t.ContainingNamespace, namedTypeSymbol.ContainingNamespace) &&
                    t.Name == namedTypeSymbol.Name))
            {
                ids.Add(namedTypeSymbol);
                return;
            }

            var members = namedTypeSymbol.GetMembers();
            foreach (var member in members)
            {
                if (member.Kind == SymbolKind.Property)
                {
                    var property = (IPropertySymbol)member;
                    var type = property.Type as INamedTypeSymbol;
                    if (type == null)
                    {
                        //type = Find(context, property.Type.Name); //在其它程序集中查找
                    }

                    if (type == null) continue;
                    TryGetIdNamedTypeSymbol(context, type, ids, allType);
                }
            }
        }


        public void Initialize(GeneratorInitializationContext context)
        {
            // Method intentionally left empty.
        }
    }
}