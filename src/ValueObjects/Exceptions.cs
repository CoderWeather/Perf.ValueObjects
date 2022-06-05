namespace Perf.ValueObjects;

public sealed class ValueObjectException : Exception {
	public ValueObjectException(string message) : base(message) { }
}

public sealed class ValueObjectValidationException<TValueObject> : Exception {
	public TValueObject Value { get; }

	public ValueObjectValidationException(TValueObject value) : base(
		$"{typeof(TValueObject).Name} is not valid with value: {value}"
	) {
		Value = value;
	}
}

public static class ValueObjectValidationException {
	public static ValueObjectValidationException<T> CreateFor<T>(T value) => new(value);
}
