using System.Reflection;

namespace Advanced_Combat_Tracker;

public interface IActLogger
{
    void Error(Exception exception, string message);

    void Verbose(Exception exception, string message);

    void Warning(string message);
}

internal sealed class ReflectionActLogger : IActLogger
{
    private readonly object target;

    public ReflectionActLogger(object target)
    {
        this.target = target;
    }

    public void Error(Exception exception, string message)
        => Invoke(nameof(Error), exception, message);

    public void Verbose(Exception exception, string message)
        => Invoke(nameof(Verbose), exception, message);

    public void Warning(string message)
        => Invoke(nameof(Warning), message);

    private void Invoke(string methodName, params object[] arguments)
    {
        var candidate = target.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name == methodName)
            .Select(method => TryBind(method, arguments))
            .Where(static binding => binding is not null)
            .OrderByDescending(static binding => binding!.Value.Score)
            .FirstOrDefault();
        if (candidate is null)
        {
            return;
        }

        try
        {
            candidate.Value.Method.Invoke(target, candidate.Value.Arguments);
        }
        catch
        {
            // Logging is a recovery path. A logger API/version mismatch must never replace
            // the parser exception that this adapter was attempting to preserve.
        }
    }

    private static MethodBinding? TryBind(MethodInfo method, object[] arguments)
    {
        var parameters = method.GetParameters();
        var hasParamArray = parameters.LastOrDefault()?.GetCustomAttribute<ParamArrayAttribute>() is not null;
        var requiredCount = parameters.Count(parameter =>
            !parameter.IsOptional && parameter.GetCustomAttribute<ParamArrayAttribute>() is null);
        if (arguments.Length < requiredCount ||
            (!hasParamArray && arguments.Length > parameters.Length))
        {
            return null;
        }

        var invokeArguments = new object?[parameters.Length];
        var score = 0;
        var fixedCount = hasParamArray ? parameters.Length - 1 : parameters.Length;
        for (var index = 0; index < Math.Min(arguments.Length, fixedCount); index++)
        {
            var argument = arguments[index];
            var parameterType = parameters[index].ParameterType;
            if (!parameterType.IsInstanceOfType(argument))
            {
                return null;
            }
            invokeArguments[index] = argument;
            score += argument.GetType() == parameterType ? 2 : 1;
        }

        for (var index = arguments.Length; index < fixedCount; index++)
        {
            if (!parameters[index].IsOptional)
            {
                return null;
            }
            invokeArguments[index] = Type.Missing;
        }

        if (hasParamArray)
        {
            var elementType = parameters[^1].ParameterType.GetElementType()
                              ?? throw new InvalidOperationException("A params parameter must be an array.");
            var remaining = Math.Max(0, arguments.Length - fixedCount);
            var values = Array.CreateInstance(elementType, remaining);
            for (var index = 0; index < remaining; index++)
            {
                var argument = arguments[fixedCount + index];
                if (!elementType.IsInstanceOfType(argument))
                {
                    return null;
                }
                values.SetValue(argument, index);
            }
            invokeArguments[^1] = values;
        }

        return new MethodBinding(method, invokeArguments, score);
    }

    private readonly record struct MethodBinding(
        MethodInfo Method,
        object?[] Arguments,
        int Score);
}
