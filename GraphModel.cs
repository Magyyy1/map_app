using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace map_app;

public class GraphDataDto
{
    [JsonPropertyName("nodes")]
    public List<NodeDto> Nodes { get; set; } = new();

    [JsonPropertyName("edges")]
    public List<EdgeDto> Edges { get; set; } = new();
}

public class NodeDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("accessible")]
    public bool? Accessible { get; set; }

    [JsonPropertyName("building")]
    public int? Building { get; set; }
}

public class EdgeDto
{
    [JsonPropertyName("from")]
    public int From { get; set; }

    [JsonPropertyName("to")]
    public int To { get; set; }

    [JsonPropertyName("weight")]
    public double Weight { get; set; }

    // Псевдонимы для обратной совместимости с редактором MapView
    [JsonIgnore]
    public int Source
    {
        get => From;
        set => From = value;
    }

    [JsonIgnore]
    public int Target
    {
        get => To;
        set => To = value;
    }

    [JsonIgnore]
    public double Time
    {
        get => Weight;
        set => Weight = value;
    }
}