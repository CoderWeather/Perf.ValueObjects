namespace Perf.ValueObjects;

public interface IValueObject<out TKey> {
	TKey Key { get; }
}

public interface IValidatableValueObject<T> : IValueObject<T>, IValidatableValueObject { }

public interface IValidatableValueObject {
	bool IsValid();
}
