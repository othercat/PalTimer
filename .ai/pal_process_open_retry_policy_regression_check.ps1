$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$policyPath = Join-Path $repoRoot 'Pal98Timer\PalProcessOpenRetryPolicy.cs'
$policySource = Get-Content -Raw -LiteralPath $policyPath

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
            Assert(!policy.ShouldPublish(101, 5, 11499, frequency),
                "same-PID access denial must remain deferred inside the grace period");
            Assert(policy.ShouldPublish(101, 5, 11500, frequency),
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
        }
    }
}
'@

Add-Type -TypeDefinition ($policySource + [Environment]::NewLine + $harnessSource)
[Pal98Timer.PalProcessOpenRetryPolicyHarness]::Run()

Write-Output 'PASS: PAL process access-denied retry is PID-scoped, bounded, resettable, and fail-closed'
