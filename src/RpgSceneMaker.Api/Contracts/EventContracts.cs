namespace RpgSceneMaker.Api.Contracts;

/// <summary>GET /events/state — id of the event whose timeline is currently running, or null. Serializes
/// to <c>{ "runningId": … }</c>.</summary>
public record EventStateDto(string? RunningId);

/// <summary>GET/POST /events/stop — whether a running timeline was stopped. Serializes to <c>{ "stopped": … }</c>.</summary>
public record EventStopDto(bool Stopped);
