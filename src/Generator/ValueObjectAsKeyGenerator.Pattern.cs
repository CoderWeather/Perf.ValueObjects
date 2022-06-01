using System.CodeDom.Compiler;

namespace Perf.ValueObjects.Generator;

internal sealed partial class ValueObjectAsKeyGenerator {
	public static void WriteDeconstruct(IndentedTextWriter writer, TypePack type) {
		var keyMembers = type.Members.Where(x => x.IsKey).ToArray();
		writer.WriteLine(
			$"public void Deconstruct({string.Join(", ", keyMembers.Select(x => $"out {x.Type.Name} {x.Symbol.Name.ToLowerInvariant()}"))})"
		);
		using (NestedScope.Start(writer)) {
			foreach (var key in keyMembers) {
				writer.WriteLine(
					$"{key.Symbol.Name.ToLowerInvariant()} = this.{key.Symbol.Name};"
				);
			}
		}
	}

	public static void WriteToString(IndentedTextWriter writer, TypePack type) {
		var singleKey = type.Members.SingleOrDefault(x => x.IsKey);
		if (singleKey is null) {
			return;
		}

		var toStringCall = singleKey.Symbol.Name is not "String" ? ".ToString()" : null;
		writer.WriteLine(
			$"public override string ToString() => this.{singleKey.Symbol.Name}{toStringCall};"
		);
	}

	public static void WriteCastSingleKeyMethods(IndentedTextWriter writer, TypePack type) {
		var key = type.Members.Single(x => x.IsKey);

		writer.WriteLine($"public static implicit operator {key.Type.Name}({type.Symbol.Name} vo)");
		using (NestedScope.Start(writer)) {
			if (type.ImplementsValidatable) {
				writer.WriteLine("if (vo.IsValid() is false)");
				using (NestedScope.Start(writer)) {
					writer.WriteLine(
						"throw ValueObjectValidationException.CreateFor(vo);"
					);
				}
			}
			else {
				writer.WriteLine($"if (vo.{key.Symbol.Name} == default)");
				using (NestedScope.Start(writer)) {
					writer.WriteLine(
						$"throw new ValueObjectAsKeyException(\"Cannot cast {type.Symbol.Name} to {key.Type.Name} when {type.Symbol.Name}.{key.Symbol.Name} == default\");"
					);
				}
			}

			writer.WriteLine();
			writer.WriteLine($"return vo.{key.Symbol.Name};");
		}

		writer.WriteLine($"public static implicit operator {type.Symbol.Name}({key.Type.Name} key)");
		using (NestedScope.Start(writer)) {
			if (type.ImplementsValidatable) {
				writer.WriteLine($"{type.Symbol.Name} vo = new(key);");
				writer.WriteLine("if (vo.IsValid() is false)");
				using (NestedScope.Start(writer)) {
					writer.WriteLine(
						"throw ValueObjectValidationException.CreateFor(vo);"
					);
				}

				writer.WriteLine();
				writer.WriteLine("return vo;");
			}
			else {
				writer.WriteLine("if (key == default)");
				using (NestedScope.Start(writer)) {
					writer.WriteLine(
						$"throw new ValueObjectAsKeyException(\"Cannot cast {key.Type.Name} to {type.Symbol.Name} when {key.Type.Name} {key.Symbol.Name} key equals default\");"
					);
				}

				writer.WriteLine();
				writer.WriteLine("return new(key);");
			}
		}
	}

	public static void WriteCastComplexKeyMethods(IndentedTextWriter writer, TypePack type) {
		var keyMembers = type.Members
		   .Where(x => x.IsKey)
		   .ToArray();
		var castToType = $"({string.Join(", ", keyMembers.Select(x => x.Type.Name))})";
		writer.WriteLine($"public static implicit operator {castToType}({type.Symbol.Name} vo)");
		using (NestedScope.Start(writer)) {
			if (type.ImplementsValidatable) {
				writer.WriteLine("if (vo.IsValid() is false)");
				using (NestedScope.Start(writer)) {
					writer.WriteLine(
						"throw ValueObjectValidationException.CreateFor(vo);"
					);
				}
			}
			else {
				writer.WriteLine(
					$"if ({string.Join(" || ", keyMembers.Select(x => $"vo.{x.Symbol.Name} == default"))})"
				);
				using (NestedScope.Start(writer)) {
					writer.WriteLine(
						$"throw new ValueObjectAsKeyException(\"Cannot cast {type.Symbol.Name} to {castToType} when any of key members equals default\");"
					);
				}
			}

			writer.WriteLine();
			writer.WriteLine(
				$"return ({string.Join(", ", keyMembers.Select(x => $"vo.{x.Symbol.Name}"))});"
			);
		}

		writer.WriteLine($"public static implicit operator {type.Symbol.Name}({castToType} key)");
		using (NestedScope.Start(writer)) {
			var tupleItems = Enumerable.Range(0, keyMembers.Length)
			   .Select(i => $"key.Item{i}")
			   .ToArray();
			if (type.ImplementsValidatable) {
				writer.WriteLine($"{type.Symbol.Name} vo = new({string.Join(", ", tupleItems)});");

				writer.WriteLine("if (vo.IsValid() is false)");
				using (NestedScope.Start(writer)) {
					writer.WriteLine(
						"throw ValueObjectValidationException.CreateFor(vo);"
					);
				}

				writer.WriteLine("return vo;");
			}
			else {
				writer.WriteLine(
					$"if ({string.Join(" || ", tupleItems.Select(x => $"{x} == default"))})"
				);
				using (NestedScope.Start(writer)) {
					writer.WriteLine(
						$"throw new ValueObjectAsKeyException(\"Cannot cast {castToType} to {type.Symbol.Name} when any of tuple elements equals default\");"
					);
				}

				writer.WriteLine();
				writer.WriteLine(
					$"return new({string.Join(", ", tupleItems)});"
				);
			}
		}
	}
}
