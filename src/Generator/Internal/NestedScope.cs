namespace Perf.ValueObjects.Generator.Internal;

internal sealed class NestedScope : IDisposable {
	private NestedScope(IndentedTextWriter writer) {
		this.writer = writer;
	}

	private readonly IndentedTextWriter writer;
	private static readonly Stack<NestedScope> Stack = new();
	private bool shouldCloseOnDispose = true;

	public void Dispose() {
		if (shouldCloseOnDispose) {
			Close();
		}

		Stack.Pop();
	}

	public void Close() {
		writer.Indent--;
		writer.WriteLine('}');
		shouldCloseOnDispose = false;
	}

	public static void CloseLast() {
		var scope = Stack.Peek();
		scope.Close();
	}

	public static NestedScope Start(IndentedTextWriter writer) {
		var scope = new NestedScope(writer);
		Stack.Push(scope);
		writer.WriteLine('{');
		writer.Indent++;
		return scope;
	}
}

/// <summary>Helper class for generating partial parts of nested types</summary>
internal sealed class NestedClassScope : IDisposable {
	private readonly List<string> containingClasses = new(0);
	private readonly IndentedTextWriter writer;

	private NestedClassScope(IndentedTextWriter writer, ISymbol classSymbol) {
		this.writer = writer;
		var containingSymbol = classSymbol.ContainingSymbol;
		while (containingSymbol.Equals(classSymbol.ContainingNamespace, SymbolEqualityComparer.Default) is false) {
			var containingNamedType = (INamedTypeSymbol)containingSymbol;
			containingClasses.Add(GetClsString(containingNamedType));
			containingSymbol = containingSymbol.ContainingSymbol;
		}

		containingClasses.Reverse();
	}

	public static NestedClassScope Start(IndentedTextWriter writer, INamedTypeSymbol cls) {
		var scope = new NestedClassScope(writer, cls);
		foreach (var containingClass in scope.containingClasses) {
			writer.WriteLine($"{containingClass}");
			writer.WriteLine("{");
			writer.Indent++;
		}

		if (cls.IsValueType) {
			writer.WriteLine("[StructLayout(LayoutKind.Sequential)]");
		}

		writer.WriteLine($"{GetClsString(cls)}");
		writer.WriteLine('{');
		writer.Indent++;

		return scope;
	}

	private static string TypeKindToStr(INamedTypeSymbol type) {
		return type switch {
			{ TypeKind: TypeKind.Class } => type.IsRecord
				? "record"
				: "class",
			{ IsValueType: true } => type.IsRecord
				? "record struct"
				: "struct",
			_ => throw new($"Unhandled kind {type} in {nameof(TypeKindToStr)}")
		};
	}

	private static string GetClsString(INamedTypeSymbol type) {
		// {public/private...} {ref} partial {class/struct} {name}
		var visibilityModifier = type.DeclaredAccessibility.ToString().ToLowerInvariant();
		var refModifier = type.IsRefLikeType ? " ref" : null;
		const string format = "{0}{1} partial {2} {3}";
		var str = string.Format(format,
			visibilityModifier,
			refModifier,
			TypeKindToStr(type),
			type.MinimalName()
		);
		return str;
	}

	public void Dispose() {
		writer.Indent--;
		writer.WriteLine('}');
		foreach (var _ in containingClasses) {
			writer.Indent--;
			writer.WriteLine("}"); // end container
		}
	}
}
