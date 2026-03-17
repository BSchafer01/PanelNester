using System.Text.Json;

namespace PanelNester.Desktop.Bridge;

public sealed class BridgeMessageDispatcher
{
    private readonly Dictionary<string, Func<JsonElement, CancellationToken, Task<object?>>> _handlers =
        new(StringComparer.Ordinal);

    public IReadOnlyList<string> RegisteredTypes =>
        _handlers.Keys.OrderBy(type => type, StringComparer.Ordinal).ToArray();

    public void Register<TRequest>(string type, Func<TRequest, CancellationToken, Task<object?>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(handler);

        if (!_handlers.TryAdd(
                type,
                async (payload, cancellationToken) =>
                {
                    var request = BridgeJson.Deserialize<TRequest>(payload);
                    return await handler(request, cancellationToken).ConfigureAwait(false);
                }))
        {
            throw new InvalidOperationException($"A bridge handler is already registered for '{type}'.");
        }
    }

    public async Task<BridgeMessageEnvelope?> DispatchAsync(
        BridgeMessageEnvelope request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Type))
        {
            return request.RequestId is null
                ? null
                : CreateResponse(
                    "bridge.invalid.response",
                    request.RequestId,
                    BridgeOperationResponse.Fault("invalid-message", "Bridge messages must include a type."));
        }

        if (!_handlers.TryGetValue(request.Type, out var handler))
        {
            return request.RequestId is null
                ? null
                : CreateResponse(
                    BridgeMessageTypes.ToResponseType(request.Type),
                    request.RequestId,
                    BridgeOperationResponse.Fault(
                        "unsupported-message",
                        $"No bridge handler is registered for '{request.Type}'."));
        }

        try
        {
            var payload = await handler(request.Payload, cancellationToken).ConfigureAwait(false);

            return request.RequestId is null
                ? null
                : CreateResponse(BridgeMessageTypes.ToResponseType(request.Type), request.RequestId, payload);
        }
        catch (BridgeDispatchException ex)
        {
            return request.RequestId is null
                ? null
                : CreateResponse(
                    BridgeMessageTypes.ToResponseType(request.Type),
                    request.RequestId,
                    BridgeOperationResponse.Fault(ex.Code, ex.Message));
        }
        catch (Exception ex)
        {
            return request.RequestId is null
                ? null
                : CreateResponse(
                    BridgeMessageTypes.ToResponseType(request.Type),
                    request.RequestId,
                    BridgeOperationResponse.Fault("host-error", ex.Message));
        }
    }

    private static BridgeMessageEnvelope CreateResponse(string type, string requestId, object? payload) =>
        new(type, requestId, BridgeJson.ToElement(payload));
}
