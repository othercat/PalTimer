// Invoke the final product DLL, not a recompiled copy of its source.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class ChallengeRelayProduct
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    static int Main(string[] args)
    {
        try {
            Assembly product = Assembly.LoadFrom(args[0]);
            if (product.GetType("Pal98.Challenges.ChallengeRules") != null || product.GetType("Pal98.Challenges.ChallengeContract") != null)
                throw new Exception("Product DLL still contains a per-rule interpreter.");
            string root = Path.GetFullPath(args[1]);
            object storage = Activator.CreateInstance(product.GetType("Pal98Timer.CompetitionStorage"), F, null,
                new object[] {Path.Combine(root, "personal"), (Func<string>)(() => "relay-fixture-device")}, null);
            using (var transport = (IDisposable)Activator.CreateInstance(product.GetType("Pal98Timer.CompetitionHttpTransport"), true))
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30))) {
                object service = Activator.CreateInstance(product.GetType("Pal98Timer.ChallengeService"), F, null,
                    new object[] {root, storage, transport, (Func<DateTimeOffset>)(() => DateTimeOffset.UtcNow)}, null);
                var task = (Task)service.GetType().GetMethod("Execute", F).Invoke(service,
                    new object[] {File.ReadAllText(args[2], new UTF8Encoding(false, true)), timeout.Token});
                task.GetAwaiter().GetResult(); object result = task.GetType().GetProperty("Result").GetValue(task);
                string json = (string)product.GetType("Pal98.Challenges.ChallengeRelayContract").GetMethod("Serialize", F).Invoke(null, new[] {result});
                File.WriteAllText(args[3], json, new UTF8Encoding(false));
            }
            return 0;
        } catch (Exception error) {Console.Error.WriteLine(error); return 1;}
    }
}
