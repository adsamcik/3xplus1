using CommandLine.Text;
using CommandLine;
using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;

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

        static long CountSteps(BigInteger number, CancellationToken token)
        {
            for (var i = 0L; i < long.MaxValue; i++)
            {
                if (number == 1)
                {
                    return i;
                }
                if (number.IsEven)
                {
                    number /= 2;
                }
                else
                {
                    number = number * 3 + 1;
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
            while (true)
            {
                var bytes = ThreadSafeRandom.Next(o.MinByteLength, o.MaxByteLength);
                Console.WriteLine($"Trying number with {bytes} bytes");
                var ptr = Marshal.AllocHGlobal(bytes);
                try
                {
                    Span<byte> span;
                    unsafe
                    {
                        span = new Span<byte>((byte*)ptr, bytes);
                    }
                    ThreadSafeRandom.NextBytes(span);
                    var number = new BigInteger(span);
                    if (number.Sign == -1)
                    {
                        number = -number;
                    }

                    var count = CountSteps(number, token);

                    if (token.IsCancellationRequested)
                    {
                        Console.WriteLine("Cancellation requested");
                        return;
                    }

                    if (count == -1L)
                    {
                        File.AppendAllText("found_number.txt", number.ToString());
                        Console.WriteLine($"Found number that does not come to 0");
                    }
                    else
                    {
                        Console.WriteLine($"Tried number with {bytes} bytes and came to 1 after {count} steps");
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
                        threads.Add(Task.Factory.StartNew(() => NumberLoop(o, token), token));
                    }

                    await Task.WhenAll(threads);

                });
        }

        public static class ThreadSafeRandom
        {
            private static readonly Random _global = new();
            [ThreadStatic]
            private static Random _local;

            public static void EnsureInitialized()
            {
                if (_local == default)
                {
                    int seed;
                    lock (_global)
                    {
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
