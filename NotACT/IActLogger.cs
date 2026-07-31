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
        var parameterTypes = arguments.Select(argument => argument.GetType()).ToArray();
        var method = target.GetType().GetMethod(
                         methodName,
                         BindingFlags.Instance | BindingFlags.Public,
                         binder: null,
                         parameterTypes,
                         modifiers: null)
                     ?? target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                         .FirstOrDefault(candidate =>
                             candidate.Name == methodName &&
                             candidate.GetParameters().Length == arguments.Length);
        method?.Invoke(target, arguments);
    }
}
