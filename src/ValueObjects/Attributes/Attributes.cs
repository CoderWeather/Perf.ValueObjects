namespace Perf.ValueObjects.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class ValueObject : Attribute {
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public sealed class Key : Attribute { }
}
