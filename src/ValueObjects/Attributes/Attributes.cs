namespace Perf.ValueObjects.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class ValueObjectAsKey : Attribute {
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public sealed class Key : Attribute { }
}
