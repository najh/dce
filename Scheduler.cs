
public static class Scheduler
{
    public static void Process<T>(IEnumerable<T> ie, int delay, Func<T, Task<bool>> job)
    {
        const int concurrency = 4;

        var count = 0;
        var total = ie.Count();

        var mutex = new object();
        void ReportProgress(int current)
        {
            lock (mutex)
            {
                if (current == count)
                {
                    var progress = Math.Round((float)current / total * 100, 2);
                    Console.WriteLine($"{current}/{total}: {progress}%");
                }
            }
        }

        Parallel.ForEach(ie, new ParallelOptions() { MaxDegreeOfParallelism = concurrency }, x =>
        {
            var success = false;
            var didWork = true;
            const int retries = 15;
            for (var i = 0; i < retries; i++)
            {
                try
                {
                    didWork = job(x).GetAwaiter().GetResult();
                    success = true;
                    break;
                }
                finally
                {
                    Thread.Sleep(delay);
                }
            }
            if (!success)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Task failed :(");
                Console.ForegroundColor = ConsoleColor.Gray;
            }
            ReportProgress(Interlocked.Increment(ref count));
            if (didWork) Thread.Sleep(delay);
        });
    }
}
