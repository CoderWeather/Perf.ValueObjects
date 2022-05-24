namespace Perf.ValueObjects;

public sealed class ValueObjectAsKeyException : Exception {
	public ValueObjectAsKeyException(string message) : base(message) { }
}
