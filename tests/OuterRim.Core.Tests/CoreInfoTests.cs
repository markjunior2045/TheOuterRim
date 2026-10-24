using Xunit;

namespace OuterRim.Core.Tests
{
    public class CoreInfoTests
    {
        [Fact]
        public void Name_IsTheCoreAssemblyName()
        {
            Assert.Equal("OuterRim.Core", CoreInfo.Name);
        }
    }
}
