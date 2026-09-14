$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$policyPath = Join-Path $repoRoot 'Pal98Timer\PalProcessOpenRetryPolicy.cs'
$policySource = Get-Content -Raw -LiteralPath $policyPath -Encoding UTF8

$harnessSource = @'
namespace Pal98Timer
{
    public static class PalProcessOpenRetryPolicyHarness
    {
        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new System.InvalidOperationException(message);
            }
        }

        public static void Run()
        {
            const long frequency = 1000;
            PalProcessOpenRetryPolicy policy = new PalProcessOpenRetryPolicy();

            Assert(!policy.ShouldPublish(101, 5, 10000, frequency),
                "first access denial must be deferred");
            Assert(!policy.ShouldPublish(101, 5, 19499, frequency),
                "same-PID access denial must remain deferred inside the grace period");
            Assert(policy.ShouldPublish(101, 5, 19500, frequency),
                "persistent same-PID access denial must publish at the grace boundary");

            policy.Reset();
            Assert(!policy.ShouldPublish(101, 5, 20000, frequency),
                "reset must start a fresh grace period");
            Assert(policy.ShouldPublish(101, 87, 20001, frequency),
                "non-access-denied errors must publish immediately");
            Assert(!policy.ShouldPublish(101, 5, 20002, frequency),
                "a non-access-denied result must reset the pending denial");

            Assert(!policy.ShouldPublish(202, 5, 21000, frequency),
                "a replacement PID must start its own grace period");
            Assert(!policy.ShouldPublish(202, 5, 20999, frequency),
                "a backwards timestamp must restart the grace period");
            Assert(policy.ShouldPublish(0, 5, 22000, frequency),
                "invalid PID must fail closed and publish");
            Assert(policy.ShouldPublish(202, 5, 22000, 0),
                "invalid timestamp frequency must fail closed and publish");

            const string elevated = "elevation mismatch";
            foreach (bool? target in new bool?[] { null, false, true })
            foreach (bool? timer in new bool?[] { null, false, true })
            {
                string message = PalProcessOpenRetryPolicy.DescribeFailure(5, false, target, timer, elevated);
                Assert((message == elevated) == (target == true && timer == false), "only confirmed token mismatch can request elevation");
                Assert(PalProcessOpenRetryPolicy.DescribeFailure(5, true, target, timer, elevated) == "", "exited process must stay silent");
            }
            Assert(PalProcessOpenRetryPolicy.DescribeFailure(87, false, true, false, elevated) != elevated,
                "other errors are never elevation mismatches");
            using (var self = System.Diagnostics.Process.GetCurrentProcess())
            {
                string message = policy.DescribeFailure(self.Id, 5, elevated);
                Assert(message.Length > 0 && message != elevated, "actual self process token cannot mismatch itself");
                policy.ClearPublishedMessage(ref message);
                Assert(message == "", "successful attach clears pending access error");
                policy.DescribeFailure(self.Id, 5, elevated);
                message = "unrelated profile error";
                policy.ClearPublishedMessage(ref message);
                Assert(message == "unrelated profile error", "do not clear unrelated diagnostics");
            }
            Assert(policy.DescribeFailure(int.MaxValue, 5, elevated) == "", "nonexistent PID is silent");
        }
    }
}
'@

Add-Type -TypeDefinition ($policySource + [Environment]::NewLine + $harnessSource)
[Pal98Timer.PalProcessOpenRetryPolicyHarness]::Run()

Write-Output 'PASS: PAL process access-denied retry is PID-scoped, bounded, resettable, and fail-closed'
