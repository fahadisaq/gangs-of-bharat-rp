using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace GangsOfBharat.Tests
{
    /// <summary>Marks a public static void method with no parameters as a test.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestAttribute : Attribute
    {
    }

    public sealed class AssertionException : Exception
    {
        public AssertionException(string message) : base(message)
        {
        }
    }

    public static class Assert
    {
        public static void True(bool condition, string message = "expected true")
        {
            if (!condition)
            {
                throw new AssertionException(message);
            }
        }

        public static void False(bool condition, string message = "expected false")
        {
            if (condition)
            {
                throw new AssertionException(message);
            }
        }

        public static void Equal<T>(T expected, T actual, string message = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new AssertionException((message == null ? string.Empty : message + ": ") +
                    "expected <" + expected + "> but was <" + actual + ">");
            }
        }

        public static void NotNull(object value, string message = "expected non-null")
        {
            if (value == null)
            {
                throw new AssertionException(message);
            }
        }

        public static void Near(double expected, double actual, double tolerance, string message = null)
        {
            if (double.IsNaN(actual) || Math.Abs(expected - actual) > tolerance)
            {
                throw new AssertionException((message == null ? string.Empty : message + ": ") +
                    "expected " + expected.ToString(CultureInfo.InvariantCulture) + " +/- " +
                    tolerance.ToString(CultureInfo.InvariantCulture) + " but was " + actual.ToString(CultureInfo.InvariantCulture));
            }
        }

        public static void AtMost(double limit, double actual, string message = null)
        {
            if (actual > limit)
            {
                throw new AssertionException((message == null ? string.Empty : message + ": ") +
                    "expected <= " + limit.ToString(CultureInfo.InvariantCulture) + " but was " + actual.ToString(CultureInfo.InvariantCulture));
            }
        }

        public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message = null)
        {
            List<T> e = expected.ToList();
            List<T> a = actual.ToList();
            if (e.Count != a.Count || !e.SequenceEqual(a))
            {
                throw new AssertionException((message == null ? string.Empty : message + ": ") +
                    "sequences differ (expected " + e.Count + " items, got " + a.Count + ")");
            }
        }
    }

    /// <summary>
    /// Minimal test runner (no NuGet test framework is available offline). Discovers [Test]
    /// methods by reflection, prints PASS/FAIL per test and exits 1 on any failure.
    /// Optional argument: a substring filter on "Class.Method".
    /// </summary>
    public static class TestRunner
    {
        public static int Main(string[] args)
        {
            string filter = args.Length > 0 ? args[0] : null;
            List<MethodInfo> tests = typeof(TestRunner).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Where(m => m.GetCustomAttribute<TestAttribute>() != null)
                .Where(m => filter == null || (m.DeclaringType.Name + "." + m.Name).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(m => m.DeclaringType.Name, StringComparer.Ordinal)
                .ThenBy(m => m.MetadataToken)
                .ToList();

            int passed = 0;
            List<string> failures = new List<string>();
            Stopwatch total = Stopwatch.StartNew();
            foreach (MethodInfo test in tests)
            {
                string name = test.DeclaringType.Name + "." + test.Name;
                Stopwatch sw = Stopwatch.StartNew();
                try
                {
                    test.Invoke(null, null);
                    passed++;
                    Console.WriteLine("PASS " + name + " (" + sw.ElapsedMilliseconds + " ms)");
                }
                catch (TargetInvocationException e)
                {
                    Exception inner = e.InnerException ?? e;
                    failures.Add(name);
                    Console.WriteLine("FAIL " + name + ": " + inner.GetType().Name + ": " + inner.Message);
                    if (!(inner is AssertionException))
                    {
                        Console.WriteLine(inner.StackTrace);
                    }
                }
            }
            Console.WriteLine();
            Console.WriteLine(passed + " passed, " + failures.Count + " failed, " + tests.Count + " total in " +
                total.ElapsedMilliseconds + " ms");
            if (tests.Count == 0)
            {
                Console.WriteLine("No tests matched.");
                return 1;
            }
            return failures.Count == 0 ? 0 : 1;
        }
    }
}
