namespace Perf.ValueObjects.Generator;

public partial class ValueObjectGenerator {
	private static void WriteBodyFromInterfaceDefinition(IndentedTextWriter writer, TypePack type) {
		var keyType = type.InterfaceMarker!.TypeArguments[0];
		var vo = type.Symbol;

		writer.WriteLineNoTabs(string.Format(ValueObjectFromValidatableInterfacePattern,
			vo.Accessibility(),
			vo.IsRecord ? "record struct" : "struct",
			vo.MinimalName(),
			keyType.MinimalName(),
			type.MarkedWithValidatableInterface ? null : "private bool IsValid() => value != default;"
		));
	}

	private const string ValueObjectFromValidatableInterfacePattern = @"
{0} partial {1} {2} {{
	public {2}() {{
		value = default;
		init = false;
	}}
	public {2}({3} value) {{
		this.value = value;
		init = true;
		if (IsValid() is false) throw ValueObjectException.Validation(this);
	}}
	[ValueObject.Key]
	private readonly {3} value;
	public {3} Value => init ? value : throw ValueObjectException.Empty<{2}>();
	{3} IValueObject<{3}>.Key => value;
	private readonly bool init;
	
	public static implicit operator {3}({2} vo) => vo.Value;
	
	public static explicit operator {2}({3} value) => new(value);
	{4}
	public override string ToString() => Value.ToString();
	public override int GetHashCode() => Value.GetHashCode();
}}";
}
