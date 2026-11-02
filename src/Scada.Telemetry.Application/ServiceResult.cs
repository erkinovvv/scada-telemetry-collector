namespace Scada.Telemetry.Application;

/// <summary>
/// Результат операции сервиса: либо значение, либо ошибки валидации.
/// Ошибки валидации — ожидаемый сценарий, поэтому возвращаем их, а не бросаем исключение
/// (исключения дорогие и под нагрузкой заметно бьют по пропускной способности).
/// </summary>
public readonly struct ServiceResult<T>
{
    private ServiceResult(T? value, Dictionary<string, string[]>? errors)
    {
        Value = value;
        Errors = errors;
    }

    public T? Value { get; }

    public Dictionary<string, string[]>? Errors { get; }

    public bool IsSuccess => Errors is null;

    public static ServiceResult<T> Success(T value) => new(value, null);

    public static ServiceResult<T> Invalid(Dictionary<string, string[]> errors) => new(default, errors);

    public static ServiceResult<T> Invalid(string key, string message) =>
        new(default, new Dictionary<string, string[]>(1) { [key] = [message] });
}
