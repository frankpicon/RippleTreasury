using Xunit;

// Each fixture starts real databases/brokers. Avoid exhausting a developer's Docker engine
// with simultaneous fixture startup. Concurrency tests still issue parallel HTTP requests.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
