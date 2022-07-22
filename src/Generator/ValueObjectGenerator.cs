using System.CodeDom.Compiler;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Perf.ValueObjects.Generator.Internal;

namespace Perf.ValueObjects.Generator;

[Generator]
public sealed partial class ValueObjectGenerator : IIncrementalGenerator {
	private const string ValueObjectAttributeMetadataName = "Perf.ValueObjects.Attributes.ValueObject";
	private const string KeyAttributeMetadataName = "Perf.ValueObjects.Attributes.ValueObject+Key";
	private const string ValidatableInterfaceMetadataName = "Perf.ValueObjects.IValidatableValueObject";

	public void Initialize(IncrementalGeneratorInitializationContext context) {
#if DEBUG
		if (Debugger.IsAttached is false) {
			Debugger.Launch();
		}
#endif
		var valueObjects = context.SyntaxProvider
		   .CreateSyntaxProvider(SyntaxFilter, SyntaxTransform)
		   .Where(x => x is not null)
		   .Select((nts, ct) => nts!)
		   .Collect();

		context.RegisterSourceOutput(valueObjects, CodeGeneration);
	}

	private static bool SyntaxFilter(SyntaxNode node, CancellationToken ct) {
		if (node is ClassDeclarationSyntax cls) {
			var haveAttribute = cls.AttributeLists
			   .Any(x => x.Attributes.Any(y => y.Name.ToString() is "ValueObject"));
			var havePartialKeyword = cls.Modifiers.Any(x => x.IsKind(SyntaxKind.PartialKeyword));
			var haveAbstractKeyword = cls.Modifiers.Any(x => x.IsKind(SyntaxKind.AbstractKeyword));
			return haveAttribute && havePartialKeyword && haveAbstractKeyword is false;
		}

		if (node is RecordDeclarationSyntax rec) {
			var haveAttribute = rec.AttributeLists
			   .Any(x => x.Attributes.Any(y => y.Name.ToString() is "ValueObject"));
			var havePartialKeyword = rec.Modifiers.Any(x => x.IsKind(SyntaxKind.PartialKeyword));
			var haveAbstractKeyword = rec.Modifiers.Any(x => x.IsKind(SyntaxKind.AbstractKeyword));
			return haveAttribute && havePartialKeyword && haveAbstractKeyword is false;
		}

		return false;
	}

	private static TypePack? SyntaxTransform(GeneratorSyntaxContext context, CancellationToken ct) {
		var valueObjectAttributeType = context.SemanticModel.Compilation
		   .GetTypeByMetadataName(ValueObjectAttributeMetadataName);
		var keyAttributeType = context.SemanticModel.Compilation
		   .GetTypeByMetadataName(KeyAttributeMetadataName);
		var validatableInterfaceType = context.SemanticModel.Compilation
		   .GetTypeByMetadataName(ValidatableInterfaceMetadataName);

		if (valueObjectAttributeType is null || validatableInterfaceType is null) {
			return null;
		}

		var symbol = context.Node switch {
			ClassDeclarationSyntax cls  => context.SemanticModel.GetDeclaredSymbol(cls, ct),
			RecordDeclarationSyntax rec => context.SemanticModel.GetDeclaredSymbol(rec, ct),
			_                           => null
		};

		if (symbol is null) {
			return null;
		}

		if (symbol.TryGetAttribute(valueObjectAttributeType) is not { } attributeData) {
			return null;
		}

		var addEqualityOperators = attributeData.NamedArguments
		   .FirstOrDefault(x => x.Key is "AddEqualityOperators")
		   .Value.Value is true;

		var addExtensionMethod = attributeData.NamedArguments
		   .FirstOrDefault(x => x.Key is "AddExtensionMethod")
		   .Value.Value is true;

		var fields = symbol.GetMembers()
		   .OfType<IFieldSymbol>()
		   .Where(f => f is {
				AssociatedSymbol: null,
				IsConst: false,
				IsStatic: false
			})
		   .Select(f => new FieldPack(f) {
				IsKey = f.GetAttributes()
				   .Any(a => a.AttributeClass?.Equals(keyAttributeType, SymbolEqualityComparer.Default) ?? false)
			})
		   .ToArray();
		var properties = symbol.GetMembers()
		   .OfType<IPropertySymbol>()
		   .Where(p => p is {
				IsStatic: false,
				IsIndexer: false
			})
		   .Select(p => new PropertyPack(p) {
				IsKey = p.GetAttributes()
				   .Any(a => a.AttributeClass?.Equals(keyAttributeType, SymbolEqualityComparer.Default) ?? false)
			})
		   .ToArray();

		if (fields.Length == 0 && properties.Length == 0) {
			return null;
		}

		var pack = new TypePack(symbol) {
			AddEqualityOperators = addEqualityOperators,
			AddExtensionMethod = addExtensionMethod
		};
		pack.Members.AddRange(fields);
		pack.Members.AddRange(properties);

		if (pack.Members.Any(x => x.IsKey) is false) {
			foreach (var m in pack.Members) {
				m.IsKey = true;
			}
		}

		if (pack.Symbol.Interfaces.Any(x => x.Equals(validatableInterfaceType, SymbolEqualityComparer.Default))) {
			pack.ImplementsValidatable = true;
		}

		var typeConstructors = pack.Symbol.InstanceConstructors;
		var possibleKeyConstructor = typeConstructors
		   .FirstOrDefault(x => x.Parameters.Length == pack.Members.Count(y => y.IsKey));

		if (possibleKeyConstructor is not null) {
			if (possibleKeyConstructor.Parameters.Length is 1
			 && pack.Members.SingleOrDefault(x => x.IsKey) is { } singleKey) {
				pack.HaveConstructorWithKey = singleKey.Type.Name
				 == possibleKeyConstructor.Parameters[0].Type.Name;
			} else {
				var typeKeyTypeNames = pack.Members
				   .Select(x => x.Type.Name)
				   .ToHashSet();
				var possibleConstructorArgsTypeNames = possibleKeyConstructor.Parameters
				   .Select(x => x.Type.Name)
				   .ToHashSet();
				pack.HaveConstructorWithKey = typeKeyTypeNames.SetEquals(possibleConstructorArgsTypeNames);
			}
		}

		if (pack.HaveConstructorWithKey is false && pack.Symbol.IsRecord) {
			return null;
		}

		return pack;
	}

	private static void CodeGeneration(SourceProductionContext context, ImmutableArray<TypePack> types) {
		if (types.IsDefaultOrEmpty) {
			return;
		}

		var typesGroupedByNamespace = types
		   .ToLookup(x => x.Symbol.ContainingNamespace, SymbolEqualityComparer.Default);

		foreach (var a in typesGroupedByNamespace) {
			var group = a.ToArray();
			var ns = a.Key!.ToString()!;
			var sourceCode = ProcessTypes(ns, group);
			if (sourceCode is null) {
				continue;
			}

			context.AddSource($"Perf.ValueObjects.Generator_{ns}.cs", SourceText.From(sourceCode, Encoding.UTF8));
		}
	}

	internal sealed class TypePack {
		public INamedTypeSymbol Symbol { get; }
		public List<BaseMemberPack> Members { get; } = new();
		public bool HaveConstructorWithKey { get; set; }
		public bool ImplementsValidatable { get; set; }
		public bool AddEqualityOperators { get; set; }
		public bool AddExtensionMethod { get; set; }

		public TypePack(INamedTypeSymbol type) {
			Symbol = type;
		}
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

	private static string? ProcessTypes(string containingNamespace, TypePack[] types) {
		var writer = new IndentedTextWriter(new StringWriter(), "    ");

		writer.WriteLines(
			"// <auto-generated />",
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
				if (type.Members.Count(x => x.IsKey) is 1) {
					WriteCastSingleKeyMethods(writer, type);
					WriteToString(writer, type);
					if (type.AddEqualityOperators) {
						WriteEqualityOperators(writer, type);
					}
				} else {
					WriteCastComplexKeyMethods(writer, type);
				}

				if (type.HaveConstructorWithKey is false) {
					WriteConstructorForKeys(writer, type);
				}
			}
		}

		var forExtensions = types
		   .Where(x => x.Members.Count(y => y.IsKey) is 1 && x.AddExtensionMethod)
		   .ToArray();

		if (forExtensions.Any()) {
			writer.WriteLine("public static class __ValueObjectsExtensions");
			using (NestedScope.Start(writer)) {
				foreach (var t in forExtensions) {
					var key = t.Members.Single(x => x.IsKey);
					writer.WriteLine(
						$"{t.Symbol.DeclaredAccessibility.ToString().ToLower()} static {t.Symbol.QualifiedName()} To{t.Symbol.Name}(this {key.Type.QualifiedName()} key) => new(key);"
					);
				}
			}
		}

		var resultSourceCode = writer.InnerWriter.ToString();
		return resultSourceCode;
	}
}
