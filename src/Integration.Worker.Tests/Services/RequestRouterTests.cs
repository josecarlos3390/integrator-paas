using FluentAssertions;
using Integration.Shared.Clients;
using Integration.Shared.Connectors;
using Integration.Shared.Domain;
using Integration.Shared.Dtos;
using Integration.Worker.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Integration.Worker.Tests.Services;

public class RequestRouterTests
{
    [Fact]
    public void CanRoute_KnownRoutes_ReturnsTrue()
    {
        var factory = new Mock<ITenantClientFactory>();
        var router = new RequestRouter(factory.Object, NullLogger<RequestRouter>.Instance);

        router.CanRoute("account", "CRM").Should().BeTrue();
        router.CanRoute("vendor", "CRM").Should().BeTrue();
        router.CanRoute("invoice", "CRM").Should().BeTrue();
        router.CanRoute("order", "ERP").Should().BeTrue();
        router.CanRoute("price_list", "CRM").Should().BeTrue();
    }

    [Fact]
    public void CanRoute_UnknownRoute_ReturnsFalse()
    {
        var factory = new Mock<ITenantClientFactory>();
        var router = new RequestRouter(factory.Object, NullLogger<RequestRouter>.Instance);

        router.CanRoute("unknown", "CRM").Should().BeFalse();
        router.CanRoute("account", "UNKNOWN").Should().BeFalse();
    }

    [Fact]
    public async Task RouteAsync_AccountToCrm_DelegatesToConnector()
    {
        var crmConnector = new Mock<ICrmConnector>();
        crmConnector.Setup(x => x.CreateCustomerAsync(It.IsAny<CrmCustomerPayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CrmApiResponse<object> { StatusCode = System.Net.HttpStatusCode.OK });

        var factory = new Mock<ITenantClientFactory>();
        factory.Setup(x => x.GetCrmConnectorAsync("tenant-001")).ReturnsAsync(crmConnector.Object);

        var router = new RequestRouter(factory.Object, NullLogger<RequestRouter>.Instance);

        var request = new IntegrationRequest
        {
            TenantId = "tenant-001",
            EntityType = "account",
            TargetSystem = "CRM",
            Payload = """
                {"entry":{"messages":[{"ExternalId":"C00001","Name":"Test Customer"}]}}
                """
        };

        var result = await router.RouteAsync(request);

        result.Should().Be("C00001");
        crmConnector.Verify(x => x.CreateCustomerAsync(It.IsAny<CrmCustomerPayload>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RouteAsync_VendorToCrm_DelegatesToConnector()
    {
        var crmConnector = new Mock<ICrmConnector>();
        crmConnector.Setup(x => x.CreateVendorAsync(It.IsAny<CrmCustomerPayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CrmApiResponse<object> { StatusCode = System.Net.HttpStatusCode.OK });

        var factory = new Mock<ITenantClientFactory>();
        factory.Setup(x => x.GetCrmConnectorAsync("tenant-001")).ReturnsAsync(crmConnector.Object);

        var router = new RequestRouter(factory.Object, NullLogger<RequestRouter>.Instance);

        var request = new IntegrationRequest
        {
            TenantId = "tenant-001",
            EntityType = "vendor",
            TargetSystem = "CRM",
            Payload = """
                {"entry":{"messages":[{"ExternalId":"V00001","Name":"Test Vendor","Type":"csupplier"}]}}
                """
        };

        var result = await router.RouteAsync(request);

        result.Should().Be("V00001");
        crmConnector.Verify(x => x.CreateVendorAsync(It.IsAny<CrmCustomerPayload>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RouteAsync_InvoiceToCrm_DelegatesToConnector()
    {
        var crmConnector = new Mock<ICrmConnector>();
        crmConnector.Setup(x => x.CreateInvoiceAsync(It.IsAny<CrmInvoicePayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CrmApiResponse<object> { StatusCode = System.Net.HttpStatusCode.OK });

        var factory = new Mock<ITenantClientFactory>();
        factory.Setup(x => x.GetCrmConnectorAsync("tenant-001")).ReturnsAsync(crmConnector.Object);

        var router = new RequestRouter(factory.Object, NullLogger<RequestRouter>.Instance);

        var request = new IntegrationRequest
        {
            TenantId = "tenant-001",
            EntityType = "invoice",
            TargetSystem = "CRM",
            Payload = """
                {"entry":{"messages":[{"ExternalId":"INV-001","CustomerId":"C00001","Date":"2024-01-01","TotalAmount":100.0}]}}
                """
        };

        var result = await router.RouteAsync(request);

        result.Should().Be("INV-001");
        crmConnector.Verify(x => x.CreateInvoiceAsync(It.IsAny<CrmInvoicePayload>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RouteAsync_UnsupportedRoute_ThrowsNotSupportedException()
    {
        var factory = new Mock<ITenantClientFactory>();
        var router = new RequestRouter(factory.Object, NullLogger<RequestRouter>.Instance);

        var request = new IntegrationRequest
        {
            EntityType = "unknown",
            TargetSystem = "CRM"
        };

        await Assert.ThrowsAsync<NotSupportedException>(() => router.RouteAsync(request));
    }
}
