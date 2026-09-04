// HTTP 轮询协议探查。
using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition.Probers;

public sealed class HttpProtocolProber(
    IHttpClientFactory httpClientFactory,
    IAcquisitionSecretResolver secrets,
    AcquisitionHttpEgressPolicy httpEgressPolicy) : IProtocolProber
{
    public string Protocol => AcquisitionProtocols.HttpPolling;

    public async Task<ProbeSnapshot> ProbeAsync(
        AcquisitionDeployment deployment,
        SourceDiscoveryQuery discovery,
        CancellationToken ct)
    {
        var connection = deployment.Task.HttpPolling;
        var requestUri = HttpAcquisitionRequestFactory.CreateEndpoint(
            connection.BaseUrl, connection.SnapshotPath);
        await httpEgressPolicy.EnsureAllowedAsync(
            requestUri,
            ct,
            connection.HeaderSecretRefs.Count > 0).ConfigureAwait(false);
        using var request = HttpAcquisitionRequestFactory.Create(
            requestUri,
            connection.Method,
            connection.RequestBody,
            connection.ContentType,
            connection.Headers,
            connection.HeaderSecretRefs,
            secrets);
        using var response = await httpClientFactory.CreateClient("device-http-acquisition")
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var snapshot = await HttpJsonSnapshotReader.ReadAsync(response.Content, ct).ConfigureAwait(false);
        var mappingsValidated = true;
        if (!AcquisitionProbeSupport.IsDiscoveryProbe(deployment))
        {
            try
            {
                HttpPollingSnapshotMapper.Map(
                    snapshot,
                    JsonAcquisitionOptionsFactory.Create(deployment),
                    deployment.Task.Source,
                    previousProcessSpecificationIdentity: null);
            }
            catch (InvalidDataException)
            {
                mappingsValidated = false;
            }
        }
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        var points = new List<AcquisitionProbePoint>();
        AcquisitionProbeSupport.FlattenJson(snapshot, string.Empty, values, points);
        return new ProbeSnapshot(values, points, mappingsValidated);
    }
}
