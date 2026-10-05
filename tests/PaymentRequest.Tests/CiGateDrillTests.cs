using Xunit;

public class CiGateDrillTests
{
    [Fact]
    public void IntentionalFailureForCiGateDrill()
    {
        Assert.Fail("Intentional CI drill: deployment must stop.");
    }
}