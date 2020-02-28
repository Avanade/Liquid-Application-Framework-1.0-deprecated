// Copyright (c) Avanade Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using AutoFixture;
using FluentValidation;
using Liquid.Base;
using Liquid.Base.Domain;
using Liquid.Domain;
using Liquid.Domain.Base;
using Liquid.Interfaces;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Liquid.Activation.Tests
{
    public class LightWorkerTests
    {
        private readonly Fixture _fixture = new Fixture();

        public LightWorkerTests()
        {
            Workbench.Instance.Reset();
            Workbench.Instance.AddToCache(WorkbenchServiceType.Telemetry, Substitute.For<ILightTelemetry>());
        }

        [Fact]
        public void InitializeWhenMockLightWorkerPresentThenQueueAndTopicsAreDiscovered()
        {
            var sut = new MockLightWorker();
            sut.Initialize();

            Assert.Contains(
                MockLightWorker.TopicList,
                _ => _.MethodInfo.ReflectedType == typeof(MockLightWorker)
                && _.MethodInfo.Name == nameof(MockLightWorker.TopicMethod));

            Assert.Contains(
                MockLightWorker.QueueList,
                _ => _.MethodInfo.ReflectedType == typeof(MockLightWorker)
                && _.MethodInfo.Name == nameof(MockLightWorker.QueueMethod));

            // Given the static nature of LightWorker, we couldn't make this an isolated assertion
            // TODO: Refactor LightWorker and then make this isolated
            Assert.Throws<LightException>(() => new MockLightWorker().Initialize());
        }

        [Theory]
        [InlineData("anything")]
        [InlineData(null)]
        [InlineData(1)]
        public void InvokeProcessWhenMethodIsNullReturnsExpectedValue(object message)
        {
            MethodsCollection.Value = _fixture.Create<string>();

            var method = typeof(MethodsCollection).GetMethod(nameof(MethodsCollection.ConstantMethod));

            var actual = LightWorker.InvokeProcess(method, ToJsonByteStream(message));

            Assert.Equal(MethodsCollection.Value, actual);
        }

        [Fact]
        public void InvokeProcessWhenMethodIsEchoMessageIsEmptyReturnsNull()
        {
            var method = typeof(MethodsCollection).GetMethod(nameof(MethodsCollection.EchoMethod));

            var actual = LightWorker.InvokeProcess(method, Array.Empty<byte>());

            Assert.Null(actual);
        }

        [Fact]
        public void InvokeProcessWhenMethodHasOneParametersAndMessageIsntValidJsonThrows()
        {
            var method = typeof(MethodsCollection).GetMethod(nameof(MethodsCollection.EchoMethod));

            var message = ToJsonByteStream("anything");

            message[0] = _fixture.Create<byte>();

            Assert.ThrowsAny<Exception>(() => LightWorker.InvokeProcess(method, message));
        }

        [Fact]
        public void InvokeProcessWhenMethodHasZeroParametersAndMessageIsntValidReturnsExpectedValue()
        {
            var method = typeof(MethodsCollection).GetMethod(nameof(MethodsCollection.ConstantMethod));

            var message = ToJsonByteStream("anything");

            message[0] = _fixture.Create<byte>();

            MethodsCollection.Value = _fixture.Create<string>();

            var actual = LightWorker.InvokeProcess(method, message);

            Assert.Equal(MethodsCollection.Value, actual);
        }

        [Fact]
        public void InvokeProcessWhenMessageIsValidJsonParsesItCorrectly()
        {
            // ARRANGE
            var anonymous = new Foobar { Foo = "Bar" };
            var anonymousAsByteStream = ToJsonByteStream(anonymous);

            var method = typeof(MethodsCollection).GetMethod(nameof(MethodsCollection.EchoMethod));

            // ACT
            var actual = (Foobar)LightWorker.InvokeProcess(method, anonymousAsByteStream);

            // ASSERT
            Assert.Equal(anonymous.Foo, actual.Foo);
        }

        [Fact]
        public void InvokeProcessWhenMethodHasZeroParametersDoesntParseMessage()
        {
            // ARRANGE
            var mi = typeof(MethodsCollection).GetMethod(nameof(MethodsCollection.ConstantMethod));

            // ACT
            var actual = (string)LightWorker.InvokeProcess(mi, null);

            // ASSERT
            Assert.Equal(MethodsCollection.Value, actual);
        }

        [Fact]
        public void InvokeProcessWhenMethodThrowsAsyncThrows()
        {
            // ARRANGE
            var mi = typeof(MethodsCollection).GetMethod(nameof(MethodsCollection.ThrowsAsync));

            var anonymous = new Foobar { Foo = "Bar" };
            var anonymousAsByteStream = ToJsonByteStream(anonymous);

            // ACT & ASSERT
            Assert.ThrowsAsync<MethodsCollection.TestException>(() => (Task)LightWorker.InvokeProcess(mi, anonymousAsByteStream));
        }

        [Fact]
        public void FactoryWhenNoErrorsReturnsInstanceOfT()
        {
            var sut = new MockLightWorker();
            Assert.IsAssignableFrom<MockDomain>(sut.Factory<MockDomain>());
        }

        [Fact]
        public void FactoryWhenNoErrorsThenDomainInstanceHasTelemetrySet()
        {
            var sut = new MockLightWorker();
            var actual = sut.Factory<MockDomain>();

            Assert.NotNull(actual.Telemetry);
        }

        [Fact]
        public void FactoryWhenNoErrorsThenDomainInstanceHasCacheSet()
        {
            // ARRANGE
            var cache = Substitute.For<ILightCache>();
            Workbench.Instance.AddToCache(WorkbenchServiceType.Cache, cache);

            // ACT
            var sut = new MockLightWorker();
            var actual = sut.Factory<MockDomain>();

            // ASSERT
            Assert.NotNull(actual.Cache);
        }

        [Fact]
        public void FactoryWhenNoErrorsThenDomainInstanceHasCriticHandlerSet()
        {
            // ARRANGE
            var sut = new MockLightWorker();
            sut.ValidateInput(new MockViewModel());

            // ACT
            var actual = sut.Factory<MockDomain>();

            // ASSERT
            Assert.NotNull(actual.CritictHandler);
        }

        [Fact]
        public void FactoryWhenViewModelHasErrorsThrows()
        {
            // ARRANGE
            var sut = new MockLightWorker();
            var viewModel = new MockViewModel
            {
                Age = string.Empty,
            };

            sut.ValidateInput(viewModel);

            // ACT & ASSERT
            Assert.ThrowsAny<Exception>(() => sut.Factory<MockDomain>());
        }

        [Fact]
        public void ValidateInputWhenViewModelHasErrorsThenViewModelErrorsAreSet()
        {
            // ARRANGE
            var sut = new MockLightWorker();
            var viewModel = new MockViewModel
            {
                Age = string.Empty,
            };

            // ACT
            sut.ValidateInput(viewModel);

            // ASSERT
            Assert.NotEmpty(viewModel.InputErrors);
        }

        [Fact]
        public void ValidateInputWhenPropertyHasErrorsThenViewModelErrorsAreSet()
        {
            var sut = new MockLightWorker();
            var viewModel = new MockViewModel
            {
                ComplexProperty = new ComplexViewModel
                {
                    DateOfBirth = DateTime.Now + TimeSpan.FromDays(1),
                },
            };

            sut.ValidateInput(viewModel);

            Assert.NotEmpty(viewModel.InputErrors);
        }

        [Fact]
        public void ValidateInputWhenPropertyIsListAndHasErrorsThenViewModelErrorsAreSet()
        {
            var sut = new MockLightWorker();
            var viewModel = new MockViewModel
            {
                List = new List<ComplexViewModel>
                {
                    new ComplexViewModel { DateOfBirth = DateTime.Now + TimeSpan.FromDays(1) },
                },
            };

            sut.ValidateInput(viewModel);

            Assert.NotEmpty(viewModel.InputErrors);
        }

        // TODO: There are translation errors in the Critics class:(
        [Fact]
        public void TerminateWhenCriticsHandlerHasCritics()
        {
            // ARRANGE
            var sut = new MockLightWorker();
            var domain = sut.Factory<MockDomain>();

            domain.CritictHandler.Critics.Add(Substitute.For<ICritic>());

            var domainResponse = new DomainResponse();

            // ACT & ASSERT
            Assert.Throws<BusinessValidationException>(() => sut.Terminate(domainResponse));
        }

        // TODO: There are translation errors in the Critics class:(
        [Fact]
        public void TerminateWhenNoCriticsReturnsTheDomainResponse()
        {
            // ARRANGE
            var sut = new MockLightWorker();
            var domainResponse = new DomainResponse();

            // ACT
            var actual = sut.Terminate(domainResponse);

            // ASSERT
            Assert.Same(actual, domainResponse);
        }

        /// <summary>
        /// Serialize any object to a JSON string and then convert it to a bytestream.
        /// </summary>
        /// <param name="obj">The object to serialize.</param>
        /// <returns>A byestream containing the object as UTF8 bytes.</returns>
        private byte[] ToJsonByteStream(object obj)
        {
            var anonymousAsString = JsonConvert.SerializeObject(obj);
            var anonymousAsByteStream = Encoding.UTF8.GetBytes(anonymousAsString);

            return anonymousAsByteStream;
        }

        [SuppressMessage(
           "Design",
           "CA1034:Nested types should not be visible",
           Justification = "Must be public so LightWorker can access the class")]
        public class Foobar
        {
            public string Foo { get; set; } = "Bar";
        }

        [SuppressMessage(
           "Design",
           "CA1034:Nested types should not be visible",
           Justification = "Must be public so LightWorker can access the class")]
        [MessageBus("asd")]
        public class MockLightWorker : LightWorker
        {
            public static List<(MethodInfo MethodInfo, TopicAttribute TopicAttribute)> TopicList => _topics
                .Select(kvp => (kvp.Key, kvp.Value))
                .ToList();

            public static List<(MethodInfo MethodInfo, QueueAttribute QueueAttribute)> QueueList => _queues
                .Select(kvp => (kvp.Key, kvp.Value))
                .ToList();

            [Topic("name", "subscriptionName", 10, true)]
            public static void TopicMethod()
            {
            }

            [Queue("name")]
            public static void QueueMethod()
            {
            }

            // exposed method for testing
            public new T Factory<T>()
                where T : LightDomain, new()
            {
                return base.Factory<T>();
            }

            public new void ValidateInput<T>(T viewModel)
               where T : LightViewModel<T>, new()
            {
                base.ValidateInput(viewModel);
            }

            public new object Terminate(DomainResponse foo)
            {
                return base.Terminate(foo);
            }
        }

        [SuppressMessage(
          "Design",
          "CA1034:Nested types should not be visible",
          Justification = "Must be public so LightWorker can access the class")]
        public class ComplexViewModel : LightViewModel<ComplexViewModel>
        {
            public DateTime DateOfBirth { get; set; } = DateTime.Now - TimeSpan.FromDays(1);

            public override void Validate()
            {
                RuleFor(_ => _.DateOfBirth).LessThan(DateTime.Now);
            }
        }

        private class MethodsCollection
        {
            public static string Value { get; set; } = "string";

            public string ConstantMethod()
            {
                return Value;
            }

            public Foobar EchoMethod(Foobar foobar)
            {
                return foobar;
            }

            [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Method must have a parameter because of the test case.")]
            public Task ThrowsAsync(Foobar foobar)
            {
                return Task.FromException(new TestException(string.Empty));
            }

            // Used to test throwing from a method
            public class TestException : Exception
            {
                public TestException(string message)
                    : base(message)
                {
                }

                public TestException(string message, Exception innerException)
                    : base(message, innerException)
                {
                }

                public TestException()
                {
                }
            }
        }

        private class MockDomain : LightService
        {
        }

        private class MockViewModel : LightViewModel<MockViewModel>
        {
            public string Age { get; set; } = "13";

            public ComplexViewModel ComplexProperty { get; set; }

            public List<ComplexViewModel> List { get; set; }

            public override void Validate()
            {
                RuleFor(_ => _.Age).NotEmpty();
            }
        }
    }
}
