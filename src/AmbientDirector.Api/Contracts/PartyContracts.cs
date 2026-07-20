using AmbientDirector.Api.Models;

namespace AmbientDirector.Api.Contracts;

// Wire envelope for GET /party/list. The API serializes PartyMember/Enemy/PartyCounter straight through (the
// entity IS the wire contract, like Board/Screen — there is no separate per-entity DTO); this record pairs the
// player roster with the table-level counters and the encounter's enemy roster (issue #120) in one response.
// Wire property names are camelCase — "players" + "counters" + "enemies" — matching issue #88's "players"
// vocabulary. The panel mirrors this exact shape by hand in AmbientDirector.Ui/Contracts/PartyContracts.cs —
// keep the two in sync when a field changes.
public record PartyDto(List<PartyMember> Players, List<PartyCounter> Counters, List<Enemy> Enemies);
