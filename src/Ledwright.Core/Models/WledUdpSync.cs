using System.Text.Json.Serialization;

namespace Ledwright.Core.Models;

/// <summary>WLED-to-WLED state broadcast settings.</summary>
public sealed class WledUdpSync
{
    [JsonPropertyName("send")] public bool? Send { get; set; }
    [JsonPropertyName("recv")] public bool? Receive { get; set; }
    [JsonPropertyName("sgrp")] public int? SendGroups { get; set; }
    [JsonPropertyName("rgrp")] public int? ReceiveGroups { get; set; }

    /// <summary>Send true alongside a state change to suppress the sync broadcast for that change only.</summary>
    [JsonPropertyName("nn")] public bool? NoNotify { get; set; }
}
