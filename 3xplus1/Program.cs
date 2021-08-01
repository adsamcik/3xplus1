using CommandLine.Text;
using CommandLine;
using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using System.Diagnostics;

namespace _3xplus1
{
    class Program
    {
        public class Options
        {
            [Option('t', "threads", Required = false, HelpText = "Set number of threads.", Default = 4)]
            public int Threads { get; set; }

            [Option("min-bytes", Required = false, HelpText = "Minimal number of bytes per number.", Default = 232)]
            public int MinByteLength { get; set; }

            [Option("max-bytes", Required = false, HelpText = "Maximum number of bytes per number", Default = 1024 * 256)]
            public int MaxByteLength { get; set; }
        }

        // Prevents unecessary allocations
        private readonly static BigInteger Two = new(2);
        private readonly static BigInteger Three = new(3);
        private readonly static BigInteger One = BigInteger.One;
        private readonly static string FilePath = "found_number.txt";

        static long CountSteps(BigInteger number, CancellationToken token)
        {
            for (var i = 1L; i < long.MaxValue; i++)
            {
                // Unfortunately creates a lot of copies
                // https://github.com/dotnet/runtime/issues/29378 should solve it (hopefully in .NET 7)
                if (number.IsEven)
                {
                    number /= Two;
                }
                else
                {
                    number = number * Three + One;
                }

                if (number == 1)
                {
                    return i;
                }

                if (token.IsCancellationRequested)
                {
                    return -1;
                }
            }
            return -1;
        }

        static void NumberLoop(Options o, CancellationToken token)
        {
            var sw = new Stopwatch();
            while (true)
            {
                var bytes = ThreadSafeRandom.Next(o.MinByteLength, o.MaxByteLength);
                Console.WriteLine($"Trying number with {bytes} bytes");
                // Allocates pointer with up to int.MaxNumber of bytes.
                var ptr = Marshal.AllocHGlobal(bytes);
                try
                {
                    Span<byte> span;
                    // Working with pointers so needs to be unsafe
                    unsafe
                    {
                        // Assigns the allocated memory to span
                        span = new Span<byte>((byte*)ptr, bytes);
                    }
                    ThreadSafeRandom.NextBytes(span);
                    var number = new BigInteger(span);

                    // Safer than flipping a byte because of endiness and possible differences in implementations
                    // Unfortunately duplicates the number, but this is not much of an issue.
                    if (number.Sign == -1)
                    {
                        number = -number;
                    }

                    sw.Restart();
                    // Run the operation
                    var count = CountSteps(number, token);

                    sw.Stop();

                    if (token.IsCancellationRequested)
                    {
                        Console.WriteLine("Cancellation requested");
                        return;
                    }

                    if (count == -1L)
                    {
                        // In this case there is basically no chance for race condition to occur, but for the completeness.
                        lock (FilePath)
                        {
                            File.AppendAllText("found_number.txt", number.ToString());
                        }
                        Console.WriteLine($"Found number that does not come to 0. Took {new TimeSpan(sw.ElapsedTicks)}.");
                    }
                    else
                    {
                        Console.WriteLine($"Tried number with {bytes} bytes and came to 1 after {count} steps. Took {new TimeSpan(sw.ElapsedTicks)} and {new TimeSpan(sw.ElapsedTicks / count)} per step.");
                    }

                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
        }

        static async Task Main(string[] args)
        {
            var random = new Random();
            var source = new CancellationTokenSource();
            var token = source.Token;

            await Parser.Default
                .ParseArguments<Options>(args)
                .WithParsedAsync(async o =>
                {
                    if (o.MinByteLength < 1 || o.MaxByteLength < 1)
                    {
                        Console.WriteLine("Minimum and maximum byte lengths must be positive");
                        return;
                    }
                    else if (o.MaxByteLength < o.MinByteLength)
                    {
                        Console.WriteLine("Max must be larger then min");
                        return;
                    }

                    var threads = new List<Task>();
                    for (int i = 0; i < o.Threads; i++)
                    {
                        // Creates new threads.
                        threads.Add(Task.Factory.StartNew(() => NumberLoop(o, token), token));
                    }

                    // Awaits all tasks to finish
                    await Task.WhenAll(threads);

                });
        }

        /// <summary>
        /// Based on response from.
        /// BlueRaja - Danny Pflughoeft
        /// https://stackoverflow.com/a/11109361
        /// </summary>
        public static class ThreadSafeRandom
        {
            private static readonly Random _global = new();

            // Ensures that each thread has its own local value
            [ThreadStatic]
            private static Random _local;

            public static void EnsureInitialized()
            {
                if (_local == default)
                {
                    int seed;
                    lock (_global)
                    {
                        // Prevents all threads to generate the same random values
                        seed = _global.Next();
                    }

                    _local = new Random(seed);
                }
            }

            public static int Next(int min, int max)
            {
                EnsureInitialized();
                return _local.Next(min, max);
            }

            public static void NextBytes(Span<byte> buffer)
            {
                EnsureInitialized();
                _local.NextBytes(buffer);
            }
        }
    }
}
