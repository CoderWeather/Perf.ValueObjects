﻿namespace Perf.ValueObjects.Generator.Internal;

internal static class SymbolsExtensions {
	#region Types

	public static string Accessibility(this ITypeSymbol type) =>
		type.DeclaredAccessibility switch {
			Microsoft.CodeAnalysis.Accessibility.Public               => "public",
			Microsoft.CodeAnalysis.Accessibility.Internal             => "internal",
			Microsoft.CodeAnalysis.Accessibility.Protected            => "protected",
			Microsoft.CodeAnalysis.Accessibility.ProtectedAndInternal => "protected internal",
			Microsoft.CodeAnalysis.Accessibility.Private              => "private",
			_                                                         => throw new ArgumentOutOfRangeException(nameof(type.DeclaredAccessibility))
		};

	public static bool IsPrimitive(this ITypeSymbol type) =>
		type.IsValueType
	 || type.IsValueNullable()
	 || type.IsEnum()
	 || type.Name is "String";

	public static bool IsString(this ITypeSymbol type) => type.Name is "String";

	public static bool IsList(this ITypeSymbol type) => type.Name is "List";

	public static bool IsValueNullable(this ITypeSymbol type) => type is INamedTypeSymbol { Name: "Nullable", IsValueType: true };

	public static bool IsEnum(this ITypeSymbol type) => type.IsValueType && type.TypeKind is TypeKind.Enum;

	public static ITypeSymbol? IfValueNullableGetInnerType(this ITypeSymbol type) =>
		type.IsValueNullable() && type is INamedTypeSymbol nt
			? nt.TypeArguments[0]
			: null;

	public static string? AsString(this TypedConstant tc) => tc.Value as string;

	public static T? As<T>(this TypedConstant tc) => tc.Value is T v ? v : default;

	#endregion

	#region Base Classes

	public static bool HasBaseClass(this ITypeSymbol type) => type.BaseType?.Name is not "Object";

	#endregion

	#region Attributes

	public static AttributeData? TryGetAttribute(this ISymbol? type, INamedTypeSymbol? attributeType) {
		return type?.GetAttributes()
		   .SingleOrDefault(a => a.AttributeClass?.Equals(attributeType, SymbolEqualityComparer.Default) ?? false);
	}

	public static AttributeData GetAttribute(this ISymbol type, INamedTypeSymbol attribute) =>
		type is not null
			? type.TryGetAttribute(attribute) ?? throw new($"{attribute} attribute not found for {type}")
			: throw new ArgumentNullException(nameof(type));

	#endregion
}
