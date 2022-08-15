namespace Perf.ValueObjects;

public interface IValueObject<out TKey> {
	TKey Key { get; }
}

public interface IValidatableValueObject<T> : IValueObject<T> {
	bool IsValid(T value);
}

public interface IValidatableValueObject {
	bool IsValid();
}
