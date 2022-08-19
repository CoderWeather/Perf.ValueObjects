namespace Perf.ValueObjects.Generator.Internal;

internal static class Extensions {
	public static void WriteLines(this IndentedTextWriter writer, params string?[] strings) {
		foreach (var s in strings) {
			writer.WriteLine(s);
		}
	}

	public static HashSet<T> ToHashSet<T>(this IEnumerable<T> enumerable) => new(enumerable);

	public static string MinimalName(this ISymbol symbol) => symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

	private static readonly SymbolDisplayFormat GlobalFormat = new(
		SymbolDisplayGlobalNamespaceStyle.Included,
		SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
		SymbolDisplayGenericsOptions.IncludeTypeParameters,
		miscellaneousOptions:
		SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
	  | SymbolDisplayMiscellaneousOptions.UseSpecialTypes
	  | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

	public static string GlobalName(this ITypeSymbol type) {
		var nullablePostfix =
			type is {
				NullableAnnotation: NullableAnnotation.Annotated,
				IsReferenceType: true
			}
				? "?"
				: null;
		return $"{type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}{nullablePostfix}";
	}

	public static INamedTypeSymbol? TryGetInterface(this INamedTypeSymbol nts, INamedTypeSymbol i) {
		foreach (var t in nts.Interfaces) {
			if (t.OriginalDefinition.Equals(i, SymbolEqualityComparer.Default)) {
				return t;
			}
		}

		return null;
	}
}
