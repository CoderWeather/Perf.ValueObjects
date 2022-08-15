namespace Perf.ValueObjects.Generator;

public partial class ValueObjectGenerator {
	private static void WriteBodyFromInterfaceDefinition(IndentedTextWriter writer, TypePack type) {
		var keyType = type.Symbol.Interfaces
		   .FirstOrDefault(x => x.OriginalDefinition.Name is "IValueObject")
		  ?.TypeArguments[0];
		if (keyType is null) {
			return;
		}

		var vo = type.Symbol;

		writer.WriteLineNoTabs(string.Format(ValueObjectFromInterfacePattern,
			vo.Accessibility(),
			vo.IsRecord ? "record struct" : "struct",
			vo.MinimalName(),
			keyType.MinimalName()
		));
	}

	private const string ValueObjectFromInterfacePattern = @"
{0} partial {1} {2} {{
	public {2}() {{
		Value = default;
		init = false;
	}}
	public {2}({3} value) {{
		Value = value;
		init = true;
		if (InternalValidation() is false) throw ValueObjectException.Validation(this);
	}}
	[ValueObject.Key]
	public readonly {3} Value;
	{3} IValueObject<{3}>.Key => Value;
	private readonly bool init;
	
	private bool InternalValidation() => init && Value != default;
	
	public static implicit operator {3}({2} vo) {{
		if (vo.InternalValidation() is false) throw ValueObjectException.Validation(vo);
		return vo.Value;
	}}
	
	public static explicit operator {2}({3} value) => new(value);
	
	public override string ToString() => Value.ToString();
	public override int GetHashCode() => Value.GetHashCode();
}}";

	private static void WriteBodyFromValidatableInterfaceDefinition(IndentedTextWriter writer, TypePack type) {
		var keyType = type.Symbol.Interfaces
		   .FirstOrDefault(x => x.OriginalDefinition.Name is "IValidatableValueObject")
		  ?.TypeArguments[0];
		if (keyType is null) {
			return;
		}

		var vo = type.Symbol;
		writer.WriteLineNoTabs(string.Format(ValueObjectFromValidatableInterfacePattern,
			vo.Accessibility(),
			vo.IsRecord ? "record struct" : "struct",
			vo.MinimalName(),
			keyType.MinimalName()
		));
	}

	private const string ValueObjectFromValidatableInterfacePattern = @"
{0} partial {1} {2} {{
	public {2}() {{
		Value = default;
		init = false;
	}}
	public {2}({3} value) {{
		Value = value;
		init = true;
		if (InternalValidation() is false) throw ValueObjectException.Validation(this);
	}}
	[ValueObject.Key]
	public readonly {3} Value;
	{3} IValueObject<{3}>.Key => Value;
	private readonly bool init;
	
	private bool InternalValidation() => init && IsValid(Value);
	
	public static implicit operator {3}({2} vo) {{
		if (vo.InternalValidation() is false) throw ValueObjectException.Validation(vo);
		return vo.Value;
	}}
	
	public static explicit operator {2}({3} value) => new(value);
	
	public override string ToString() => Value.ToString();
	public override int GetHashCode() => Value.GetHashCode();
}}";
}
