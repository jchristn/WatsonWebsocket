using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Test.Automated
{
    public enum TestResult
    {
        Pass,
        Fail,
        Skip
    }

    public class TestOutcome
    {
        public string TestName { get; set; }
        public string Category { get; set; }
        public TestResult Result { get; set; }
        public string FailureReason { get; set; }
        public TimeSpan Duration { get; set; }

        public override string ToString()
        {
            string status = Result switch
            {
                TestResult.Pass => "PASS",
                TestResult.Fail => "FAIL",
                TestResult.Skip => "SKIP",
                _ => "UNKNOWN"
            };

            string durationStr = $"{Duration.TotalMilliseconds:F2}ms";
            string result = $"[{status}] {Category}/{TestName} ({durationStr})";

            if (Result == TestResult.Fail && !string.IsNullOrEmpty(FailureReason))
            {
                result += $"\n        Reason: {FailureReason}";
            }

            return result;
        }
    }

    public class TestRunner
    {
        private readonly List<TestOutcome> _results = new List<TestOutcome>();
        private readonly Stopwatch _overallStopwatch = new Stopwatch();

        public int TotalTests => _results.Count;
        public int PassedTests => _results.FindAll(r => r.Result == TestResult.Pass).Count;
        public int FailedTests => _results.FindAll(r => r.Result == TestResult.Fail).Count;
        public int SkippedTests => _results.FindAll(r => r.Result == TestResult.Skip).Count;
        public TimeSpan OverallDuration => _overallStopwatch.Elapsed;

        public void StartOverallTimer()
        {
            _overallStopwatch.Start();
        }

        public void StopOverallTimer()
        {
            _overallStopwatch.Stop();
        }

        public async Task RunTestAsync(string category, string testName, Func<Task> testAction)
        {
            var outcome = new TestOutcome
            {
                TestName = testName,
                Category = category
            };

            var stopwatch = Stopwatch.StartNew();

            try
            {
                await testAction();
                outcome.Result = TestResult.Pass;
            }
            catch (SkipTestException ex)
            {
                outcome.Result = TestResult.Skip;
                outcome.FailureReason = ex.Message;
            }
            catch (Exception ex)
            {
                outcome.Result = TestResult.Fail;
                outcome.FailureReason = ex.Message;
            }
            finally
            {
                stopwatch.Stop();
                outcome.Duration = stopwatch.Elapsed;
            }

            _results.Add(outcome);
            Console.WriteLine(outcome);
        }

        public void RunTest(string category, string testName, Action testAction)
        {
            var outcome = new TestOutcome
            {
                TestName = testName,
                Category = category
            };

            var stopwatch = Stopwatch.StartNew();

            try
            {
                testAction();
                outcome.Result = TestResult.Pass;
            }
            catch (SkipTestException ex)
            {
                outcome.Result = TestResult.Skip;
                outcome.FailureReason = ex.Message;
            }
            catch (Exception ex)
            {
                outcome.Result = TestResult.Fail;
                outcome.FailureReason = ex.Message;
            }
            finally
            {
                stopwatch.Stop();
                outcome.Duration = stopwatch.Elapsed;
            }

            _results.Add(outcome);
            Console.WriteLine(outcome);
        }

        public void PrintSummary()
        {
            Console.WriteLine();
            Console.WriteLine(new string('=', 80));
            Console.WriteLine("TEST SUMMARY");
            Console.WriteLine(new string('=', 80));
            Console.WriteLine();
            Console.WriteLine($"Total Tests:   {TotalTests}");
            Console.WriteLine($"Passed:        {PassedTests}");
            Console.WriteLine($"Failed:        {FailedTests}");
            Console.WriteLine($"Skipped:       {SkippedTests}");
            Console.WriteLine($"Overall Time:  {OverallDuration.TotalSeconds:F2} seconds");
            Console.WriteLine();

            if (FailedTests > 0)
            {
                Console.WriteLine(new string('-', 80));
                Console.WriteLine("FAILED TESTS:");
                Console.WriteLine(new string('-', 80));
                foreach (var result in _results)
                {
                    if (result.Result == TestResult.Fail)
                    {
                        Console.WriteLine($"  - {result.Category}/{result.TestName}");
                        Console.WriteLine($"    Reason: {result.FailureReason}");
                    }
                }
                Console.WriteLine();
            }

            if (SkippedTests > 0)
            {
                Console.WriteLine(new string('-', 80));
                Console.WriteLine($"SKIPPED TESTS ({SkippedTests}):");
                Console.WriteLine(new string('-', 80));
                // Show first 5 skipped tests, then summarize
                int shown = 0;
                string commonReason = null;
                foreach (var result in _results)
                {
                    if (result.Result == TestResult.Skip)
                    {
                        if (shown < 5)
                        {
                            Console.WriteLine($"  - {result.Category}/{result.TestName}");
                            shown++;
                        }
                        if (commonReason == null)
                            commonReason = result.FailureReason;
                    }
                }
                if (SkippedTests > 5)
                {
                    Console.WriteLine($"  ... and {SkippedTests - 5} more");
                }
                if (commonReason != null)
                {
                    Console.WriteLine($"\n  Common reason: {commonReason}");
                }
                Console.WriteLine();
            }

            Console.WriteLine(new string('=', 80));
            string overallResult;
            if (FailedTests > 0)
            {
                overallResult = "FAIL";
            }
            else if (PassedTests == 0 && SkippedTests > 0)
            {
                overallResult = "SKIP (all tests skipped)";
            }
            else
            {
                overallResult = "PASS";
            }
            Console.WriteLine($"OVERALL RESULT: {overallResult}");
            if (SkippedTests > 0 && FailedTests == 0)
            {
                Console.WriteLine($"({SkippedTests} tests skipped - not counted as failures)");
            }
            Console.WriteLine(new string('=', 80));

            if (SkippedTests > 0)
            {
                Console.WriteLine();
                Console.WriteLine("NOTE: Some tests were skipped due to connectivity issues.");
                Console.WriteLine("      To run all tests, please re-run with elevated permissions:");
                Console.WriteLine("      - Windows: Run as Administrator");
                Console.WriteLine("      - Linux/macOS: Run with sudo");
                Console.WriteLine();
            }
        }

        public int GetExitCode()
        {
            return FailedTests == 0 ? 0 : 1;
        }
    }

    public class SkipTestException : Exception
    {
        public SkipTestException(string message) : base(message) { }
    }

    public static class Assert
    {
        public static void IsTrue(bool condition, string message = null)
        {
            if (!condition)
            {
                throw new Exception(message ?? "Expected true but was false");
            }
        }

        public static void IsFalse(bool condition, string message = null)
        {
            if (condition)
            {
                throw new Exception(message ?? "Expected false but was true");
            }
        }

        public static void AreEqual<T>(T expected, T actual, string message = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception(message ?? $"Expected '{expected}' but was '{actual}'");
            }
        }

        public static void AreNotEqual<T>(T notExpected, T actual, string message = null)
        {
            if (EqualityComparer<T>.Default.Equals(notExpected, actual))
            {
                throw new Exception(message ?? $"Expected value to not be '{notExpected}'");
            }
        }

        public static void IsNotNull(object obj, string message = null)
        {
            if (obj == null)
            {
                throw new Exception(message ?? "Expected non-null value but was null");
            }
        }

        public static void IsNull(object obj, string message = null)
        {
            if (obj != null)
            {
                throw new Exception(message ?? $"Expected null but was '{obj}'");
            }
        }

        public static void IsGreaterThan<T>(T value, T threshold, string message = null) where T : IComparable<T>
        {
            if (value.CompareTo(threshold) <= 0)
            {
                throw new Exception(message ?? $"Expected value greater than '{threshold}' but was '{value}'");
            }
        }

        public static void IsGreaterThanOrEqual<T>(T value, T threshold, string message = null) where T : IComparable<T>
        {
            if (value.CompareTo(threshold) < 0)
            {
                throw new Exception(message ?? $"Expected value >= '{threshold}' but was '{value}'");
            }
        }

        public static void IsLessThan<T>(T value, T threshold, string message = null) where T : IComparable<T>
        {
            if (value.CompareTo(threshold) >= 0)
            {
                throw new Exception(message ?? $"Expected value less than '{threshold}' but was '{value}'");
            }
        }

        public static void Throws<TException>(Action action, string message = null) where TException : Exception
        {
            try
            {
                action();
                throw new Exception(message ?? $"Expected exception of type {typeof(TException).Name} but no exception was thrown");
            }
            catch (TException)
            {
                // Expected
            }
            catch (Exception ex)
            {
                throw new Exception(message ?? $"Expected exception of type {typeof(TException).Name} but got {ex.GetType().Name}: {ex.Message}");
            }
        }

        public static async Task ThrowsAsync<TException>(Func<Task> action, string message = null) where TException : Exception
        {
            try
            {
                await action();
                throw new Exception(message ?? $"Expected exception of type {typeof(TException).Name} but no exception was thrown");
            }
            catch (TException)
            {
                // Expected
            }
            catch (Exception ex) when (!(ex is Exception && ex.Message.Contains("Expected exception")))
            {
                throw new Exception(message ?? $"Expected exception of type {typeof(TException).Name} but got {ex.GetType().Name}: {ex.Message}");
            }
        }

        public static void Contains(string haystack, string needle, string message = null)
        {
            if (!haystack.Contains(needle))
            {
                throw new Exception(message ?? $"Expected '{haystack}' to contain '{needle}'");
            }
        }

        public static void DoesNotThrow(Action action, string message = null)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                throw new Exception(message ?? $"Expected no exception but got {ex.GetType().Name}: {ex.Message}");
            }
        }

        public static async Task DoesNotThrowAsync(Func<Task> action, string message = null)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                throw new Exception(message ?? $"Expected no exception but got {ex.GetType().Name}: {ex.Message}");
            }
        }

        public static void Skip(string reason)
        {
            throw new SkipTestException(reason);
        }

        public static void CollectionIsNotEmpty<T>(IEnumerable<T> collection, string message = null)
        {
            using var enumerator = collection.GetEnumerator();
            if (!enumerator.MoveNext())
            {
                throw new Exception(message ?? "Expected non-empty collection but was empty");
            }
        }

        public static void CollectionIsEmpty<T>(IEnumerable<T> collection, string message = null)
        {
            using var enumerator = collection.GetEnumerator();
            if (enumerator.MoveNext())
            {
                throw new Exception(message ?? "Expected empty collection but had elements");
            }
        }

        public static void ArraysEqual<T>(T[] expected, T[] actual, string message = null)
        {
            if (expected.Length != actual.Length)
            {
                throw new Exception(message ?? $"Array lengths differ: expected {expected.Length}, got {actual.Length}");
            }

            for (int i = 0; i < expected.Length; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(expected[i], actual[i]))
                {
                    throw new Exception(message ?? $"Arrays differ at index {i}: expected '{expected[i]}', got '{actual[i]}'");
                }
            }
        }
    }
}
