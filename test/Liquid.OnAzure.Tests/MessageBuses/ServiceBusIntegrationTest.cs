using System;
using Xunit;

namespace Liquid.OnAzure.IntegrationTests.MessageBuses
{
    public class ServiceBusIntegrationTest 
    {

        [Fact]
        public void InitializeWhenThrowsException()
        {
            Assert.ThrowsAny<ArgumentException>(() => new ServiceBus());
        }
    }
}
