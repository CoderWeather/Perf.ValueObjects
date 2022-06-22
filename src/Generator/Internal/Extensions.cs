using System.CodeDom.Compiler;
using Microsoft.CodeAnalysis;

namespace Perf.ValueObjects.Generator.Internal;

internal static class Extensions {
	public static void WriteLines(this IndentedTextWriter writer, params string?[] strings) {
		foreach (var s in strings)
			writer.WriteLine(s);
	}

	public static HashSet<T> ToHashSet<T>(this IEnumerable<T> enumerable) {
		return new(enumerable);
	}

	public static string QualifiedName(this ISymbol symbol) {
		return symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
	}
}
