namespace Perf.ValueObjects;

public interface IValueObject<in TKey> {
	Guid Key { get; }
}

public interface IValidatableValueObject<in T> : IValueObject<T> {
	bool IsValid(T value);
}

public interface IValidatableValueObject {
	bool IsValid();
}
