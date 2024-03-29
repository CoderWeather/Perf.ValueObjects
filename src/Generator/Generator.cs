﻿using System.Collections.Concurrent;

namespace Perf.ValueObjects.Generator;

[Generator]
public sealed partial class ValueObjectGenerator : IIncrementalGenerator {
	public void Initialize(IncrementalGeneratorInitializationContext context) {
		var valueObjects = context.SyntaxProvider
		   .CreateSyntaxProvider(SyntaxFilter, SyntaxTransform)
		   .Where(x => x is not null)
		   .Select((nts, ct) => nts!)
		   .Collect();

		context.RegisterSourceOutput(valueObjects, CodeGeneration);
	}

	private static bool SyntaxFilter(SyntaxNode node, CancellationToken ct) {
		if (node is RecordDeclarationSyntax rec) {
			var haveAttribute = rec.AttributeLists
			   .Any(x => x.Attributes.Any(y => y.Name.ToString() is "ValueObject"));
			var haveVoInterfaceMarker = rec.BaseList?.Types
			   .Any(x => x.Type is GenericNameSyntax { Identifier.Text: "IValueObject" or "IValidatableValueObject" }) is true;
			var recordStructDeclaration = rec.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword);
			var havePartialKeyword = rec.Modifiers.Any(SyntaxKind.PartialKeyword);
			var haveAbstractKeyword = rec.Modifiers.Any(SyntaxKind.AbstractKeyword);

			return (haveAttribute || haveVoInterfaceMarker)
			 && recordStructDeclaration
			 && havePartialKeyword
			 && haveAbstractKeyword is false;
		}

		return false;
	}

	private const string ValueObjectAttributeMetadataName = "Perf.ValueObjects.Attributes.ValueObject";
	private const string KeyAttributeMetadataName = "Perf.ValueObjects.Attributes.ValueObject+Key";
	private const string ValidatableInterfaceMetadataName = "Perf.ValueObjects.IValidatableValueObject";
	private const string ValueObjectInterfaceMetadataName = "Perf.ValueObjects.IValueObject`1";
	private const string ValueObjectValidatableInterfaceMetadataName = "Perf.ValueObjects.IValidatableValueObject`1";

	private static TypePack? SyntaxTransform(GeneratorSyntaxContext context, CancellationToken ct) {
		var valueObjectAttributeType = context.SemanticModel.Compilation.GetTypeByMetadataName(ValueObjectAttributeMetadataName);
		var keyAttributeType = context.SemanticModel.Compilation.GetTypeByMetadataName(KeyAttributeMetadataName)!;
		var validatableInterfaceType = context.SemanticModel.Compilation.GetTypeByMetadataName(ValidatableInterfaceMetadataName)!;
		var voInterfaceType = context.SemanticModel.Compilation.GetTypeByMetadataName(ValueObjectInterfaceMetadataName)!;
		var voValidatableInterfaceType = context.SemanticModel.Compilation.GetTypeByMetadataName(ValueObjectValidatableInterfaceMetadataName)!;

		if (valueObjectAttributeType is null) {
			return null;
		}

		var symbol = context.Node is RecordDeclarationSyntax rec
			? context.SemanticModel.GetDeclaredSymbol(rec, ct)
			: null;

		if (symbol is null) {
			return null;
		}

		TypePack pack;
		if (symbol.TryGetInterface(voValidatableInterfaceType) is { } i2) {
			pack = new(symbol) {
				InterfaceMarker = i2,
				MarkedWithValidatableInterface = true
			};
		} else if (symbol.TryGetInterface(voInterfaceType) is { } i) {
			pack = new(symbol) {
				InterfaceMarker = i
			};
		} else if (symbol.TryGetAttribute(valueObjectAttributeType) is { } attributeData) {
			pack = new(symbol) {
				AddEqualityOperators = attributeData.NamedArguments
				   .FirstOrDefault(x => x.Key is "AddEqualityOperators")
				   .Value.Value is true,
				AddExtensionMethod = attributeData.NamedArguments
				   .FirstOrDefault(x => x.Key is "AddExtensionMethod")
				   .Value.Value is true
			};
		} else {
			return null;
		}

		if (pack.InterfaceMarker is null && pack.MarkedWithValidatableInterface is false) {
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
		}

		return pack;
	}

	private static readonly ConcurrentQueue<(DiagnosticSeverity Type, object Message)> DiagnosticsMessages = new();

	private static void CodeGeneration(SourceProductionContext context, ImmutableArray<TypePack> types) {
		if (DiagnosticsMessages.IsEmpty is false) {
			while (DiagnosticsMessages.TryDequeue(out var msg))
				context.ReportDiagnostic(Diagnostic.Create(
					new(
						nameof(ValueObjectGenerator),
						"Title",
						msg.Message.ToString(),
						"category",
						msg.Type,
						true
					),
					null,
					(object?[]?)null
				));
		}

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

		writer.WriteLine($"namespace {containingNamespace};");

		foreach (var type in types) {
			if (type.InterfaceMarker is not null) {
				WriteBodyFromInterfaceDefinition(writer, type);
			} else {
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
						$"{t.Symbol.DeclaredAccessibility.ToString().ToLower()} static {t.Symbol.MinimalName()} To{t.Symbol.Name}(this {key.Type.MinimalName()} key) => new(key);"
					);
				}
			}
		}

		var resultSourceCode = writer.InnerWriter.ToString();
		return resultSourceCode;
	}
}
