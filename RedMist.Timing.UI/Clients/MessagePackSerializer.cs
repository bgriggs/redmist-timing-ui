using RestSharp;
using RestSharp.Serializers;
using MessagePack;
using System;

namespace RedMist.Timing.UI.Clients;

public class MessagePackRestSerializer : IRestSerializer, ISerializer, IDeserializer
{
    public string? Serialize(Parameter bodyParameter) => Serialize(bodyParameter.Value);

    public ContentType ContentType { get; set; } = ContentType.Json;

    public ISerializer Serializer => this;
    public IDeserializer Deserializer => this;
    public DataFormat DataFormat => DataFormat.Binary;
    public string[] AcceptedContentTypes => ["application/x-msgpack"];
    public SupportsContentType SupportsContentType
        => contentType => contentType.Value.EndsWith("msgpack", StringComparison.InvariantCultureIgnoreCase);

    public string Serialize(object? obj)
    {
        var bytes = MessagePackSerializer.Serialize(obj);
        return Convert.ToBase64String(bytes);
    }

    public T? Deserialize<T>(RestResponse response)
    {
        if (response.RawBytes == null || response.RawBytes.Length == 0)
            return default;
        return MessagePackSerializer.Deserialize<T>(response.RawBytes);
    }
}