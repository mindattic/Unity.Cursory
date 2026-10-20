using NUnit.Framework;

// Fixtures run side by side; tests within a fixture stay sequential.
[assembly: Parallelizable(ParallelScope.Fixtures)]
