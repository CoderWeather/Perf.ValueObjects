namespace Perf.ValueObjects;

public abstract class ValueObjectException<TValueObject> : Exception {
	protected ValueObjectException(string message) : base(message) { }
	protected ValueObjectException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class ValueObjectValidationException<TValueObject> : ValueObjectException<TValueObject> {
	public TValueObject Value { get; }

	public ValueObjectValidationException(TValueObject value) : base(
		$"{typeof(TValueObject).Name} is not valid with value: {value}"
	) {
		Value = value;
	}
}

public sealed class ValueObjectInitializationException<TValueObject> : ValueObjectException<TValueObject> {
	public ValueObjectInitializationException() : base(
		$"{typeof(TValueObject).Name} is not initialized"
	) { }
}

public static class ValueObjectException {
	public static ValueObjectValidationException<T> Validation<T>(T value) => new(value);
	public static ValueObjectInitializationException<T> Initialization<T>() => new();
}
