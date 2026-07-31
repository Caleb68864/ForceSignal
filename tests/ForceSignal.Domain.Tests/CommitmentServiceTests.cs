using ForceSignal.Domain.Rules;

namespace ForceSignal.Domain.Tests;

public sealed class CommitmentServiceTests
{
    [Fact]
    public void Verify_AcceptsOriginalNormalizedOrderAndSalt()
    {
        var commitments = new Sha256CommitmentService();
        var salt = commitments.CreateSalt();
        const string normalizedOrder = """{"velocityDelta":1,"turnSteps":0,"turnDirection":"None"}""";

        var hash = commitments.CreateHash(normalizedOrder, salt);

        Assert.True(commitments.Verify(hash, normalizedOrder, salt));
    }
}
