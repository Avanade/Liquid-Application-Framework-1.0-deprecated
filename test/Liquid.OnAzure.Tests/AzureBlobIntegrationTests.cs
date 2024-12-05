// Copyright (c) Avanade Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AutoFixture;
using AutoFixture.AutoNSubstitute;
using Liquid.Base;
using Liquid.Interfaces;
using Liquid.Repository;
using Liquid.Tests;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using NSubstitute;
using Xunit;

namespace Liquid.OnAzure.IntegrationTests
{
    public class AzureBlobIntegrationTests : IDisposable
    {
        private const string ContentType = "text/plain";
        private const string DefaultConnectionString = "UseDevelopmentStorage=true";
        private const string DefaultContainerName = "removecontainer";
        private static readonly IFixture _fixture = new Fixture().Customize(new AutoNSubstituteCustomization());

        private static readonly ILightRepository _fakeLightRepository = Substitute.For<ILightRepository>();

        private readonly string _expectedData;

        private readonly Stream _stream;

        private readonly ILightAttachment _lightAttachment;

        private readonly AzureBlob _sut;

        public AzureBlobIntegrationTests()
        {
            Workbench.Instance.Reset();

            Workbench.Instance.AddToCache(WorkbenchServiceType.Repository, _fakeLightRepository);

            _sut = new AzureBlob(new MediaStorageConfiguration
            {
                ConnectionString = DefaultConnectionString,
                Container = _fixture.Create<string>().ToLower(CultureInfo.CurrentCulture),
            });

            _expectedData = _fixture.Create<string>();

            _stream = ToMemoryStream(_expectedData);

            _lightAttachment = new LightAttachment
            {
                ContentType = ContentType,
                Id = _fixture.Create<string>() + ".txt",
                MediaLink = _fixture.Create<string>(),
                MediaStream = _stream,
                Name = _fixture.Create<string>(),
                ResourceId = _fixture.Create<string>(),
            };
        }

        public static IEnumerable<object[]> CtorWhenContainersDoesntExistsCreatesNewData { get; set; } = new[]
        {
            new object[] { BlobContainerPublicAccessType.Blob.ToString(),  BlobContainerPublicAccessType.Blob },
            new object[] { BlobContainerPublicAccessType.Off.ToString(), BlobContainerPublicAccessType.Off },
            new object[] { BlobContainerPublicAccessType.Container.ToString(), BlobContainerPublicAccessType.Container},
            new object[] { _fixture.Create<string>(), BlobContainerPublicAccessType.Blob },
        };

        [Fact]
        public void CtorWhenConfigurationIsNullThrows()
        {
            Assert.ThrowsAny<Exception>(() => new AzureBlob(null));
        }

        [Fact]
        public Task InsertUpdateAsyncWhenAttachmentIsNullThrows()
        {
            return Assert.ThrowsAnyAsync<ArgumentNullException>(() => _sut.InsertUpdateAsync(null));
        }

        [Fact]
        public async Task GetWhenAttachmentDoesntExistsThrows()
        {
            // TODO: Improve exception
            await Assert.ThrowsAnyAsync<StorageException>(async () => await _sut.GetAsync(_lightAttachment.ResourceId, _lightAttachment.Id));
        }

        [Fact]
        public async Task GetAttachmentWhenItWasInsertedReturnsInsertedData()
        {
            await _sut.InsertUpdateAsync(_lightAttachment);

            using (var attachment = await _sut.GetAsync(_lightAttachment.ResourceId, _lightAttachment.Id))
            {
                var actual = attachment.MediaStream.AsString();

                Assert.Equal(_expectedData, actual);
            }
        }

        [Fact]
        public async Task RemoveWhenAttachmentIsNullThrows()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await _sut.Remove(null));
        }

        [Fact]
        public async Task GetWhenResourceRemovedThrowsStorageException()
        {
            await _sut.InsertUpdateAsync(_lightAttachment);

            using (var attachment = await _sut.GetAsync(_lightAttachment.ResourceId, _lightAttachment.Id))
            {
                var actual = attachment.MediaStream.AsString();

                Assert.Equal(_expectedData, actual);

                await _sut.Remove(_lightAttachment);
                await Assert.ThrowsAnyAsync<StorageException>(() => _sut.GetAsync(_lightAttachment.ResourceId, _lightAttachment.Id));
            }
        }

        [Theory]
        [MemberData(nameof(CtorWhenContainersDoesntExistsCreatesNewData))]
        public async Task CtorWhenContainersDoesntExistsCreatesNew(string accessType, BlobContainerPublicAccessType expected)
        {
            // ARRANGE
            var client = CloudStorageAccount.Parse(DefaultConnectionString).CreateCloudBlobClient();
            var container = client.GetContainerReference(DefaultContainerName);

            await container.DeleteIfExistsAsync();

            var configuration = new MediaStorageConfiguration
            {
                ConnectionString = DefaultConnectionString,
                Container = DefaultContainerName,
                Permission = accessType,
            };

            // ACT
            _ = new AzureBlob(configuration);

            // ASSERT
            container = client.GetContainerReference(DefaultContainerName);

            Assert.True(await container.ExistsAsync());

            var blobContainerPermissions = await container.GetPermissionsAsync();

            Assert.Equal(expected, blobContainerPermissions.PublicAccess);
        }

        [Fact]
        public async Task CtorWhenPermissionIsUnknownThrows()
        {
            // ARRANGE
            var client = CloudStorageAccount.Parse(DefaultConnectionString).CreateCloudBlobClient();
            var container = client.GetContainerReference(DefaultContainerName);

            await container.DeleteIfExistsAsync();

            var configuration = new MediaStorageConfiguration
            {
                ConnectionString = DefaultConnectionString,
                Container = DefaultContainerName,
                Permission = BlobContainerPublicAccessType.Unknown.ToString(),
            };

            // ACT & ASSERT
            Assert.Throws<LightException>(() => new AzureBlob(configuration));
        }

        [Fact]
        public async Task CtorWhenPermissionIsOutOfRangeNumberThenPermissionIsBlob()
        {
            // ARRANGE
            var client = CloudStorageAccount.Parse(DefaultConnectionString).CreateCloudBlobClient();
            var container = client.GetContainerReference(DefaultContainerName);

            await container.DeleteIfExistsAsync();

            var configuration = new MediaStorageConfiguration
            {
                ConnectionString = DefaultConnectionString,
                Container = DefaultContainerName,
                Permission = "100",
            };

            // ACT
            _ = new AzureBlob(configuration);

            // ASSERT
            container = client.GetContainerReference(DefaultContainerName);

            Assert.True(await container.ExistsAsync());

            var blobContainerPermissions = await container.GetPermissionsAsync();

            Assert.Equal(BlobContainerPublicAccessType.Blob, blobContainerPermissions.PublicAccess);
        }

        [Theory, AutoSubstituteData]
        public async Task CtorWhenContainersDoesntExistsAndAccessTypeIsAnyStringCreatesWithAccessTypeBlob(string accessType)
        {
            // ARRANGE 
            var client = CloudStorageAccount.Parse(DefaultConnectionString).CreateCloudBlobClient();
            var container = client.GetContainerReference(DefaultContainerName);

            await container.DeleteIfExistsAsync();

            var configuration = new MediaStorageConfiguration
            {
                ConnectionString = DefaultConnectionString,
                Container = DefaultContainerName,
                Permission = accessType,
            };

            // ACT
            _ = new AzureBlob(configuration);

            // ASSERT
            container = client.GetContainerReference(DefaultContainerName);

            Assert.True(await container.ExistsAsync());

            var blobContainerPermissions = await container.GetPermissionsAsync();

            Assert.Equal(BlobContainerPublicAccessType.Blob, blobContainerPermissions.PublicAccess);
        }

        [Fact]
        public async Task CtorWhenContainersDoesntExistsAndAccessTypeIsUnknownThrows()
        {
            // ARRANGE
            var client = CloudStorageAccount.Parse(DefaultConnectionString).CreateCloudBlobClient();
            var container = client.GetContainerReference(DefaultContainerName);

            await container.DeleteIfExistsAsync();

            var configuration = new MediaStorageConfiguration
            {
                ConnectionString = DefaultConnectionString,
                Container = DefaultContainerName,
                Permission = BlobContainerPublicAccessType.Unknown.ToString(),
            };

            // ACT & ASSERT
            Assert.ThrowsAny<Exception>(() => new AzureBlob(configuration));
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                _stream?.Dispose();
                _lightAttachment?.Dispose();
            }
        }

        private static Stream ToMemoryStream(string data)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(data));
        }
    }
}
