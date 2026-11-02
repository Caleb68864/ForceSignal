namespace ForceSignal.Api.Tests;

// Environment variables belong to the process, and every WebApplicationFactory reads them when its
// host builds. A test that sets one therefore configures any host another test class builds while it
// is set: FORCESIGNAL_WEB_ORIGIN=http://board.invalid turned a Development host in ApiHardeningTests
// into one that refused even http://localhost:6297, failing CI whenever the two classes overlapped.
//
// Those tests set real variables on purpose, because what they prove is that the spellings an operator
// types reach the API through the real environment provider. So the fix is not to fake the variable
// but to stop anything else running meanwhile. xUnit runs a collection with parallelization disabled
// by itself, after all the parallel ones have finished.
//
// Any test class that calls Environment.SetEnvironmentVariable belongs in this collection.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironmentTestGroup
{
    public const string Name = "Tests that set process environment variables";
}
