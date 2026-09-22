global using Xunit;

// The headless Avalonia host is one UI thread. Letting several test classes build their own sessions
// at the same time races on that thread and fails at platform start-up rather than in a test, so the
// assembly runs its collections one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
