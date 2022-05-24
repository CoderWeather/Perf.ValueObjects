using System.CodeDom.Compiler;
using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Perf.ValueObjects.Generator.Internal;

namespace Perf.ValueObjects.Generator;

[Generator]
internal sealed partial class ValueObjectAsKeyGenerator : ISourceGenerator {
	private sealed class SyntaxReceiver : ISyntaxReceiver {
		public readonly List<TypeDeclarationSyntax> Types = new();

		public void OnVisitSyntaxNode(SyntaxNode syntaxNode) {
			switch (syntaxNode) {
				case RecordDeclarationSyntax { AttributeLists.Count: > 0 } record:
					Types.Add(record);
					break;
				case StructDeclarationSyntax { AttributeLists.Count: > 0 } s:
					Types.Add(s);
					break;
			}
		}
	}

	internal sealed class TypePack {
		public INamedTypeSymbol Symbol { get; }
		public List<BaseMemberPack> Members { get; } = new();
		public bool HaveConstructorWithKey { get; set; }

		public TypePack(INamedTypeSymbol type) => Symbol = type;
	}

	internal abstract class BaseMemberPack {
		public ISymbol Symbol { get; }
		public ITypeSymbol OriginalType { get; }
		public ITypeSymbol Type { get; }
		public bool IsKey { get; set; }

		protected BaseMemberPack(ISymbol symbol, ITypeSymbol type) {
			Symbol = symbol;
			Type = type;
			OriginalType = Type.IsDefinition ? Type : Type.OriginalDefinition;
		}
	}

	internal sealed class FieldPack : BaseMemberPack {
		public new IFieldSymbol Symbol { get; }

		public FieldPack(IFieldSymbol fieldSymbol) :
			base(fieldSymbol, fieldSymbol.Type) {
			Symbol = fieldSymbol;
		}
	}

	internal sealed class PropertyPack : BaseMemberPack {
		public new IPropertySymbol Symbol { get; }

		public PropertyPack(IPropertySymbol propertySymbol) :
			base(propertySymbol, propertySymbol.Type) {
			Symbol = propertySymbol;
		}
	}

	public void Initialize(GeneratorInitializationContext context) {
		context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
#if DEBUG
		if (Debugger.IsAttached is false) {
			Debugger.Launch();
		}
#endif
	}

	public void Execute(GeneratorExecutionContext context) {
		try {
			ExecuteInternal(context);
		}
		catch (Exception e) {
			var descriptor = new DiagnosticDescriptor(nameof(ValueObjectAsKeyGenerator),
				"Error",
				e.ToString(),
				"Error",
				DiagnosticSeverity.Error,
				true);
			var diagnostic = Diagnostic.Create(descriptor, Location.None);
			context.ReportDiagnostic(diagnostic);
		}
	}

	private void ExecuteInternal(GeneratorExecutionContext context) {
		if (context.SyntaxReceiver is not SyntaxReceiver receiver) {
			return;
		}

		var compilation = context.Compilation;

		var typeAttribute = compilation.GetTypeByMetadataName(
			"Perf.ValueObjects.Attributes.ValueObjectAsKey");
		var keyAttribute = compilation.GetTypeByMetadataName(
			"Perf.ValueObjects.Attributes.ValueObjectAsKey+Key");

		if (typeAttribute is null || keyAttribute is null) {
			return;
		}

		var typesToProcess = new Dictionary<INamedTypeSymbol, TypePack>(SymbolEqualityComparer.Default);

		foreach (var cs in receiver.Types) {
			var model = compilation.GetSemanticModel(cs.SyntaxTree);
			var symbol = model.GetDeclaredSymbol(cs);
			if (symbol is null || symbol is { IsGenericType: true }) {
				continue;
			}

			var voAttr = symbol.TryGetAttribute(typeAttribute);
			// var haveEmptyConstructor = symbol.InstanceConstructors
			// .Any(x => x.Parameters.IsDefaultOrEmpty);
			var havePartialKeyword = cs.Modifiers.Any(x => x.IsKind(SyntaxKind.PartialKeyword));
			// var haveRecordKeyword = cs.Modifiers.Any(x => x.IsKind(SyntaxKind.RecordKeyword));
			var members = symbol.GetMembers();
			var fields = members.OfType<IFieldSymbol>()
			   .Where(x => x.Name.EndsWith("BackingField") is false)
			   .ToArray();
			var props = members.OfType<IPropertySymbol>().ToArray();

			if (voAttr is null
			 || symbol.IsRefLikeType
				// || haveEmptyConstructor
			 || havePartialKeyword is false
			 || (fields.Any() is false && props.Any() is false)) {
				continue;
			}

			var pack = new TypePack(symbol);
			typesToProcess[symbol] = pack;

			foreach (var f in fields) {
				var keyAttr = f.TryGetAttribute(keyAttribute);

				pack.Members.Add(new FieldPack(f) {
					IsKey = keyAttr is not null
				});
			}

			foreach (var p in props) {
				var keyAttr = p.TryGetAttribute(keyAttribute);

				pack.Members.Add(new PropertyPack(p) {
					IsKey = keyAttr is not null
				});
			}
		}

		foreach (var (k, v) in typesToProcess.ToArray()) {
			if (v.Members.Any() is false) {
				typesToProcess.Remove(k);
			}
		}

		foreach (var (_, v) in typesToProcess) {
			if (v.Members.Any(x => x.IsKey) is false) {
				v.Members.ForEach(x => x.IsKey = true);
			}

			var typeConstructors = v.Symbol.InstanceConstructors;
			var possibleKeyConstructor = typeConstructors
			   .FirstOrDefault(x => x.Parameters.Length == v.Members.Count(y => y.IsKey));

			if (possibleKeyConstructor is not null) {
				if (v.Members.SingleOrDefault(x => x.IsKey) is { } singleKey
				 && possibleKeyConstructor.Parameters.Length is 1) {
					v.HaveConstructorWithKey = singleKey.Type.Name
					 == possibleKeyConstructor.Parameters[0].Type.Name;
				}
				else {
					var typeKeyTypeNames = v.Members
					   .Select(x => x.Type.Name)
					   .ToHashSet();
					var possibleConstructorArgsTypeNames = possibleKeyConstructor.Parameters
					   .Select(x => x.Name)
					   .ToHashSet();
					v.HaveConstructorWithKey = typeKeyTypeNames.SetEquals(possibleConstructorArgsTypeNames);
				}
			}
		}

		foreach (var group in typesToProcess
					.GroupBy(x => x.Value.Symbol.ContainingNamespace,
						 x => x.Value,
						 SymbolEqualityComparer.Default)) {
			var ns = group.Key!.ToString();
			var source = ProcessTypes(ns, group.ToArray());
			if (source is null) {
				continue;
			}

			context.AddSource($"Perf.ValueObjects.Generator_{ns}.cs", SourceText.From(source, Encoding.UTF8));
		}
	}

	private string? ProcessTypes(string containingNamespace, TypePack[] types) {
		var writer = new IndentedTextWriter(new StringWriter(), "    ");

		writer.WriteLines(
			"using System.Runtime.InteropServices;",
			"using Perf.ValueObjects;",
			"using Perf.ValueObjects.Attributes;"
		);
		writer.WriteLine();

		var nsToImport = types
		   .SelectMany(x => x.Members
			   .Select(y => y.OriginalType.ContainingNamespace)
			   .Where(y => y.ToString() != containingNamespace && y.IsGlobalNamespace is false)
			   .Select(y => y.ToString())
			)
		   .Distinct();

		foreach (var ns in nsToImport) {
			writer.WriteLine($"using {ns};");
		}

		writer.WriteLine();
		writer.WriteLine($"namespace {containingNamespace};");
		writer.WriteLine();

		foreach (var type in types) {
			using (NestedClassScope.Start(writer, type.Symbol)) {
				if (type.Members.Count(x => x.IsKey) > 1) {
					WriteCastComplexKeyMethods(writer, type);
				}
				else {
					WriteCastSingleKeyMethods(writer, type);
				}

				WriteDeconstruct(writer, type);
				WriteToString(writer, type);
			}
		}

		var resultSourceCode = writer.InnerWriter.ToString();
		return resultSourceCode;
	}
}
