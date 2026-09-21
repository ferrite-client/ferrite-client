using Xunit;

// Every test class here talks to its own loopback HTTP server, and the whole suite starting at once
// puts enough connect pressure on the loopback stack that a request occasionally fails as a transport
// error rather than reaching the assertion. That showed up as an intermittent failure in the update
// tests, which assert on the *reason* a feed was refused. Four at a time keeps the run parallel without
// making the machine compete with itself.
[assembly: CollectionBehavior(MaxParallelThreads = 4)]
