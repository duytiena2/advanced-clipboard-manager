using System.Diagnostics;
using System.Reflection;

namespace ClipboardManager.Core.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute : Attribute { }

public sealed class AssertionException : Exception
{
    public AssertionException(string message) : base(message) { }
}

public static class Assert
{
    public static void True(bool condition, string? message = null)
    {
        if (!condition) throw new AssertionException(message ?? "Expected true");
    }

    public static void False(bool condition, string? message = null) => True(!condition, message ?? "Expected false");

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new AssertionException($"{message ?? "Not equal"}: expected <{expected}> but was <{actual}>");
    }

    public static void NotNull(object? value, string? message = null) => True(value is not null, message ?? "Expected non-null");
    public static void Null(object? value, string? message = null) => True(value is null, message ?? $"Expected null but was <{value}>");
}

public static class Program
{
    public static int Main(string[] args)
    {
        var filter = args.Length > 0 ? args[0] : null;
        var tests = typeof(Program).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => m.GetCustomAttribute<TestAttribute>() is not null)
                .Select(m => (Type: t, Method: m)))
            .Where(x => filter is null || $"{x.Type.Name}.{x.Method.Name}".Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Type.Name).ThenBy(x => x.Method.Name)
            .ToList();

        int passed = 0, failed = 0;
        var sw = Stopwatch.StartNew();
        foreach (var (type, method) in tests)
        {
            var name = $"{type.Name}.{method.Name}";
            object? instance = null;
            try
            {
                instance = method.IsStatic ? null : Activator.CreateInstance(type);
                method.Invoke(instance, null);
                passed++;
                Console.WriteLine($"  PASS  {name}");
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                failed++;
                Console.WriteLine($"  FAIL  {name}\n        {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"  FAIL  {name}\n        {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                (instance as IDisposable)?.Dispose();
            }
        }
        Console.WriteLine($"\n{passed} passed, {failed} failed, {tests.Count} total ({sw.ElapsedMilliseconds} ms)");
        return failed == 0 ? 0 : 1;
    }
}
