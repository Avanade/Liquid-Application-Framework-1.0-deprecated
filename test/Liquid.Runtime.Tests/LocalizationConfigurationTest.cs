using Xunit;

namespace Liquid.Runtime.Tests
{
    public class LocalizationConfigurationTest    {
        
        [Fact]
        public void WhenEnabledModuleShouldBeMandatory()
        {
            var configuration = new LocalizationConfig();
         
            configuration.Validate();
            var results = configuration.Validator.Validate(configuration);

            Assert.False(results.IsValid);
            Assert.Contains(results.Errors,(value) => value == "A Default Culture should be defined.");
        }

 
        [Fact]
        public void WhenEnabledValidationPass()
        {
            var configuration = new LocalizationConfig();
            configuration.DefaultCulture = "pt-BR";
            string[] cultures = new string[] { "en-US", "pt-BR" };
            configuration.SupportedCultures = cultures;

            configuration.Validate();

            var results = configuration.Validator.Validate(configuration);

            Assert.True(results.IsValid);
            Assert.Empty(results.Errors);
        }
        
    }
}
